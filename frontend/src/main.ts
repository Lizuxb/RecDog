import './style.css';
import { icon } from './icons';

const $ = <T extends HTMLElement = HTMLElement>(selector: string) => document.querySelector<T>(selector)!;
const esc = (value: string) => value.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);
const tool = (name: string, label: string, contents = icon(name)) => `<button class="tool" data-action="${name}" title="${label}" aria-label="${label}">${contents}</button>`;
const brand = `<svg class="brand-mark" viewBox="0 0 32 32" aria-hidden="true"><path d="M6 3h14l6 6v20H6Z" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M20 3v7h6M11 13v8m5-11v14m5-10v7" fill="none" stroke="currentColor" stroke-width="1.6"/><path d="M6 28h20" stroke="#b68d21" stroke-width="3"/></svg>`;
$('#app').innerHTML = `
  <aside class="sidebar" aria-label="项目导航">
    <div class="brand">${brand}<div><strong>听录<span>RecDog</span></strong><small>本地文档工作台</small></div></div>
    <button class="workspace-picker" id="workspace-picker" title="项目管理">${icon('folder')}<span><small>当前项目</small><strong id="project-name">正在打开…</strong></span>${icon('arrow')}</button>
    <div class="side-section"><span>人员与资料</span><button class="icon-button" id="new-folder" title="新建人员文件夹" aria-label="新建人员文件夹">${icon('plus')}</button></div>
    <nav id="folder-list" class="folder-list" aria-label="人员文件夹"></nav>
    <div class="side-section new-document-section"><span>新建文档</span><button class="icon-button" id="new-document" title="新建文档" aria-label="新建文档">${icon('plus')}</button></div>
    <button class="side-button" id="open-project">${icon('folder')}项目管理</button>
    <div class="side-bottom">
      <button class="side-button" id="templates">${icon('template')}模板库</button>
      <button class="side-button" id="settings">${icon('settings')}设置</button>
      <div class="local-line"><span class="local-dot"></span>文件保存在本机</div>
    </div>
  </aside>
  <header class="topbar">
    <button class="icon-button mobile-menu" id="menu" aria-label="项目导航">${icon('menu')}</button>
    <div class="scope">工作台 <span>/</span> <span id="scope-name">文档</span></div>
    <div class="top-actions"><button id="import">${icon('upload')}<span>导入 DOCX</span></button><button id="export" class="primary">${icon('download')}<span>导出 DOCX</span></button></div>
  </header>
  <main class="sheet">
    <div class="document-heading"><div class="heading-copy"><div class="breadcrumb" id="document-kind">文档草稿</div><button class="title-button" id="rename" title="重命名文档"><h1 id="document-title">访谈记录</h1><span>重命名</span></button></div><div class="heading-actions"><button class="quiet" id="close-current">${icon('close')}<span>关闭</span></button><button class="quiet" id="save-template">${icon('template')}<span>另存为模板</span></button><button class="icon-button" id="toggle-outline" aria-label="收起文档大纲" aria-expanded="true" title="文档大纲">${icon('outline')}</button></div></div>
    <div class="editor-toolbar" role="toolbar" aria-label="文档编辑">
      <button id="save" class="save-button" title="保存草稿 Ctrl+S">${icon('save')}保存</button><span class="divider"></span>
      ${tool('undo', '撤销 Ctrl+Z')}${tool('redo', '重做 Ctrl+Y')}<span class="divider"></span>
      <select id="text-style" aria-label="段落样式"><option value="">正文</option><option value="first">标题 1</option><option value="second">标题 2</option><option value="third">标题 3</option></select>
      <select id="font-family" aria-label="字体"><option>宋体</option><option>微软雅黑</option><option>黑体</option><option>仿宋</option><option>楷体</option><option>Arial</option></select>
      <select id="font-size" aria-label="字号">${[12,14,16,18,20,24,26,28,32,36,48].map(n => `<option ${n === 16 ? 'selected' : ''}>${n}</option>`).join('')}</select>
      ${tool('bold', '粗体 Ctrl+B', '<b>B</b>')}${tool('italic', '斜体 Ctrl+I', '<i>I</i>')}${tool('underline', '下划线 Ctrl+U', '<u>U</u>')}<span class="divider"></span>
      ${tool('alignLeft', '左对齐')}${tool('alignCenter', '居中')}${tool('alignRight', '右对齐')}
      ${tool('bullet', '项目符号', icon('list'))}${tool('ordered', '编号列表', '<span class="number-list">1.</span>')}
      <details class="table-menu"><summary title="表格操作" aria-label="表格操作">${icon('table')}</summary><div class="dropdown"><label>行 <input id="table-rows" type="number" min="1" max="20" value="3"></label><label>列 <input id="table-cols" type="number" min="1" max="20" value="2"></label><button id="insert-table">插入表格</button><button data-action="row">在下方添加行</button><button data-action="column">在右侧添加列</button><button data-action="merge">合并选中单元格</button><button data-action="split">取消合并单元格</button><button data-action="deleteRow">删除当前行</button><button data-action="deleteColumn">删除当前列</button></div></details>
      <button class="tool search-toggle" id="toggle-search" aria-label="查找文档文字" title="查找 Ctrl+F">${icon('search')}</button>
    </div>
    <div class="search-bar" id="search-bar" hidden><label for="find">查找</label><input id="find" placeholder="输入文档中的文字"><button id="find-next">下一处</button><button class="icon-button" id="close-search" aria-label="关闭查找">${icon('close')}</button></div>
    <div id="save-error" class="inline-error" role="alert" hidden><span></span><button id="retry-save">重试保存</button></div>
    <div class="work-panes">
      <section class="editor-well" aria-label="文档工作区">
        <div class="paper-context"><span id="paper-name">访谈记录</span><span>A4 <span class="context-dot">·</span> 纵向</span></div>
        <div id="paper-scroll" class="paper-scroll"><div id="canvas-editor"></div></div>
      </section>
      <aside class="outline-panel" aria-label="文档大纲"><div class="outline-heading"><strong>文档大纲</strong><span id="outline-count"></span></div><nav id="outline-list" aria-label="文档标题"></nav><div class="outline-foot"><span>文档格式</span><strong>DOCX</strong><p>保存草稿以保留全部编辑内容；导出用于分享与打印。</p><button class="text-button" id="compatibility">查看格式兼容说明 ${icon('arrow')}</button></div></aside>
    </div>
    <footer class="statusbar"><div><span id="page-count">1 页</span><span class="footer-divider"></span><span id="word-count">— 字</span></div><button id="save-status" class="save-status" title="点击保存当前草稿"><span class="status-dot"></span><span id="save-label">正在打开文档…</span></button><div class="zoom"><label for="zoom">缩放</label><select id="zoom"><option value="0.65">65%</option><option value="0.75">75%</option><option value="0.85" selected>85%</option><option value="1">100%</option><option value="1.25">125%</option></select></div></footer>
  </main>
  <dialog id="dialog"><div class="dialog-heading"><h2 id="dialog-title"></h2><button class="icon-button" id="close-dialog" aria-label="关闭对话框">${icon('close')}</button></div><div id="dialog-content"></div></dialog>
  <div id="toast" role="status" hidden></div>`;


import { startWorkbench } from './workbench';
startWorkbench();
