import type { DocumentContent } from './bridge';
const text = (value: string, options: Record<string, unknown> = {}) => ({ value, font: '宋体', size: 16, ...options });
export const builtins = [
  { id: 'interview', title: '访谈记录', description: '基本信息、访谈内容与补充备注', create: (): DocumentContent => ({
    header: [text('访谈记录', { size: 11, color: '#666666' })],
    main: [
      text('访谈记录', { level: 'first', size: 26, bold: true, rowFlex: 'center' }),
      text('\n'),
      { type: 'table', value: '\n', colgroup: [{ width: 130 }, { width: 435 }], trList: [
        { height: 42, tdList: [{ colspan: 1, rowspan: 1, value: [text('访谈主题', { bold: true })] }, { colspan: 1, rowspan: 1, value: [text('请填写访谈主题', { color: '#666666' })] }] },
        { height: 42, tdList: [{ colspan: 1, rowspan: 1, value: [text('时间与地点', { bold: true })] }, { colspan: 1, rowspan: 1, value: [text('请填写时间与地点', { color: '#666666' })] }] },
        { height: 42, tdList: [{ colspan: 1, rowspan: 1, value: [text('参与人员', { bold: true })] }, { colspan: 1, rowspan: 1, value: [text('请填写参与人员', { color: '#666666' })] }] }
      ] },
      text('\n访谈内容', { level: 'second', size: 20, bold: true }),
      text('\n在此记录访谈内容。'),
      text('\n\n补充备注', { level: 'second', size: 20, bold: true }),
      text('\n在此填写需要补充的说明。')
    ], footer: []
  }) },
  { id: 'meeting', title: '会议记录', description: '会议主题、讨论事项与行动安排', create: (): DocumentContent => ({ header: [], main: [
    text('会议记录', { level: 'first', size: 26, bold: true, rowFlex: 'center' }),
    text('\n会议主题：\n会议时间：\n会议地点：\n参会人员：\n'),
    text('\n讨论事项', { level: 'second', size: 20, bold: true }), text('\n在此记录讨论内容。\n'),
    text('\n行动安排', { level: 'second', size: 20, bold: true }), text('\n在此填写负责人、事项与完成时间。')
  ], footer: [] }) },
  { id: 'blank', title: '空白文档', description: '从一张白纸开始', create: (): DocumentContent => ({ header: [], main: [text('')], footer: [] }) }
];
