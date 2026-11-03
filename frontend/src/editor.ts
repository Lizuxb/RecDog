import CanvasEditor, { EditorMode, RenderMode, RowFlex, TitleLevel, ListType, ListStyle, type IEditorData, type IRangeStyle, type ICatalog, type IEditorOption } from '@hufe921/canvas-editor';
import type { DocumentContent } from './bridge';
import { normalizeHeadingContent } from './heading-content';

/** Only this adapter knows canvas-editor commands and serialization. */
export class DocumentEditor {
  private instance: CanvasEditor;
  private loading = false;
  private docxReady: Promise<void> | null = null;
  onChange = () => {};
  onStyle = (_style: IRangeStyle) => {};
  onPages = (_count: number) => {};
  onScale = (_scale: number) => {};
  onUserSelection = (_range: ReturnType<DocumentEditor['captureLiveTranscriptAnchor']>) => {};
  private selectionHandler = (event: Event) => {
    if (event instanceof KeyboardEvent && (event.ctrlKey || event.metaKey || event.altKey || ['Control', 'Shift', 'Alt', 'Meta', 'Escape'].includes(event.key))) return;
    if (!(event.target instanceof Element) || !event.target.closest('canvas, textarea')) return;
    queueMicrotask(() => { try { this.onUserSelection(this.captureLiveTranscriptAnchor()); } catch { } });
  };
  constructor(private container: HTMLDivElement, content: DocumentContent, preview = false) {
    this.instance = new CanvasEditor(container, normalizeHeadingContent(content) as IEditorData, {
      defaultFont: '宋体', defaultSize: 16, width: 794, height: 1123,
      margins: [72, 72, 72, 72], pageGap: 24, scale: 0.85,
      scrollContainerSelector: '#paper-scroll',
      pageNumber: { format: '{pageNo}', size: 12, color: '#666666', rowFlex: RowFlex.CENTER },
      ...this.readOptions(content),
      // Songti rounds glyph widths at fractional sizes on Windows. Paint each glyph at its
      // measured position so the visible text and caret share the same layout coordinates.
      renderMode: RenderMode.COMPATIBILITY,
      ...(preview ? { mode: EditorMode.READONLY, scrollContainerSelector: '#history-preview-scroll', scale: 0.65 } : {})
    });
    this.instance.listener.contentChange = () => { if (!this.loading) this.onChange(); };
    this.instance.listener.rangeStyleChange = style => this.onStyle(style);
    this.instance.listener.pageSizeChange = count => this.onPages(count);
    this.instance.listener.pageScaleChange = scale => this.onScale(scale);
    this.container.addEventListener('pointerup', this.selectionHandler);
    this.container.addEventListener('keyup', this.selectionHandler);
  }
  private readOptions(content: DocumentContent): IEditorOption {
    const source = content.pageOptions as IEditorOption | undefined;
    if (!source) return {};
    const { width, height, margins, paperDirection, pageNumber, defaultFont, defaultSize } = source;
    return { width, height, margins, paperDirection, pageNumber, defaultFont, defaultSize };
  }
  load(content: DocumentContent) {
    // Recreate the editor to reset history, selection and document-level options.
    this.loading = true;
    this.destroy();
    const replacement = new DocumentEditor(this.container, content);
    replacement.onChange = () => this.onChange();
    replacement.onStyle = style => this.onStyle(style);
    replacement.onPages = count => this.onPages(count);
    replacement.onScale = scale => this.onScale(scale);
    replacement.onUserSelection = range => this.onUserSelection(range);
    this.selectionHandler = replacement.selectionHandler;
    this.instance = replacement.instance;
    this.docxReady = null;
    this.loading = false;
  }
  destroy() { this.container.removeEventListener('pointerup', this.selectionHandler); this.container.removeEventListener('keyup', this.selectionHandler); this.instance.destroy(); }
  serialize(): DocumentContent {
    const { data, options } = this.instance.command.getValue();
    const { width, height, margins, paperDirection, pageNumber, defaultFont, defaultSize } = options;
    return { ...data, pageOptions: { width, height, margins, paperDirection, pageNumber, defaultFont, defaultSize } };
  }
  async importDocx(buffer: ArrayBuffer) {
    await this.ensureDocx();
    await this.instance.command.executeImportDocx({ arrayBuffer: buffer });
  }
  private ensureDocx() {
    this.docxReady ??= import('@hufe921/canvas-editor-plugin-docx').then(plugin => { this.instance.use(plugin.default); }).catch(error => { this.docxReady = null; throw error; });
    return this.docxReady;
  }
  async exportDocx(title: string, download = true) { await this.ensureDocx(); return this.instance.command.executeExportDocx({ fileName: title.replace(/[<>:"/\\|?*]/g, '_') || '听录文档', download }); }
  async outline(): Promise<ICatalog> { return await this.instance.command.getCatalog() || []; }
  wordCount() { return this.instance.command.getWordCount(); }
  pages() { return this.instance.command.getContainer().querySelectorAll('.ce-page-container > canvas[data-index]').length || 1; }
  locate(id: string) { this.instance.command.executeLocationCatalog(id); }
  search(value: string) { this.instance.command.executeSearch(value || null); }
  nextResult() { this.instance.command.executeSearchNavigateNext(); }
  font(value: string) { this.instance.command.executeFont(value); }
  size(value: number) { this.instance.command.executeSize(value); }
  title(value: string) { this.instance.command.executeTitle(value ? value as TitleLevel : null); }
  zoom(value: number) { this.instance.command.executePageScale(value); }
  insertTranscript(text: string) {
    const range = this.instance.command.getRange();
    if (!range || !Number.isInteger(range.startIndex) || !Number.isInteger(range.endIndex) || range.startIndex < 0 || range.endIndex < range.startIndex) throw new Error('请先在文档中点击要插入文字的位置，再点击“插入到文档”。');
    if (range.isCrossRowCol) throw new Error('请在一个表格单元格内或正文中点击插入位置。');
    this.instance.command.executeInsertElementList([{ value: text.replace(/\r\n?/g, '\n') }]);
  }
  captureLiveTranscriptAnchor() {
    const range = this.instance.command.getRange();
    if (!range || !Number.isInteger(range.startIndex) || !Number.isInteger(range.endIndex) || range.startIndex < 0 || range.endIndex < range.startIndex) throw new Error('请先在文档中点击要开始转写的位置。');
    if (range.isCrossRowCol) throw new Error('实时转写暂不支持跨表格单元格的选区，请在正文或单个单元格中点击。');
    return { ...range };
  }
  replaceLiveTranscript(range: { startIndex: number; endIndex: number; tableId?: string; startTdIndex?: number; endTdIndex?: number; startTrIndex?: number; endTrIndex?: number }, _previousText: string, nextText: string) {
    const saved = { ...this.instance.command.getRange() };
    this.instance.command.executeSetPositionContext(range);
    this.instance.command.executeReplaceRange(range);
    if (nextText) this.instance.command.executeInsertElementList([{ value: nextText.replace(/\r\n?/g, '\n') }], { isReplace: true });
    else if (range.endIndex > range.startIndex) this.instance.command.executeBackspace();
    const end = this.instance.command.getRange().endIndex;
    const actual = { ...range, endIndex: end };
    // Keep the user's caret at the same logical position instead of leaving the partial selected.
    if (saved.startIndex >= 0) {
      const delta = end - range.endIndex;
      const map = (index: number) => index <= range.startIndex ? index : index >= range.endIndex ? index + delta : end;
      if (saved.tableId === range.tableId && saved.startTrIndex === range.startTrIndex && saved.startTdIndex === range.startTdIndex) {
        saved.startIndex = map(saved.startIndex); saved.endIndex = map(saved.endIndex);
      }
      this.instance.command.executeSetPositionContext(saved);
      this.instance.command.executeReplaceRange(saved);
    }
    return actual;
  }
  insertTable(rows: number, columns: number) {
    if (!Number.isInteger(rows) || !Number.isInteger(columns) || rows < 1 || rows > 20 || columns < 1 || columns > 20) throw new Error('表格行列数必须是 1 到 20 之间的整数。');
    const range = this.instance.command.getRange();
    if (!range || range.startIndex < 0) this.instance.command.executeSetRange(0, 0);
    this.instance.command.executeInsertTable(rows, columns);
  }
  action(name: string) {
    const c = this.instance.command;
    switch (name) {
      case 'bold': c.executeBold(); break;
      case 'italic': c.executeItalic(); break;
      case 'underline': c.executeUnderline(); break;
      case 'undo': c.executeUndo(); break;
      case 'redo': c.executeRedo(); break;
      case 'alignLeft': c.executeRowFlex(RowFlex.LEFT); break;
      case 'alignCenter': c.executeRowFlex(RowFlex.CENTER); break;
      case 'alignRight': c.executeRowFlex(RowFlex.RIGHT); break;
      case 'bullet': c.executeList(ListType.UL, ListStyle.DISC); break;
      case 'ordered': c.executeList(ListType.OL, ListStyle.DECIMAL); break;
      case 'row': c.executeInsertTableBottomRow(); break;
      case 'column': c.executeInsertTableRightCol(); break;
      case 'merge': c.executeMergeTableCell(); break;
      case 'split': c.executeCancelMergeTableCell(); break;
      case 'deleteRow': c.executeDeleteTableRow(); break;
      case 'deleteColumn': c.executeDeleteTableCol(); break;
    }
  }
}
