// Exercises the actual export dialog, DOCX conversion and native folder writer in an isolated project.
document.querySelector('#export').click();
for (let i = 0; i < 100 && !document.querySelector('#export-tree'); i++) await new Promise(resolve => setTimeout(resolve, 50));
if (!document.querySelector('#export-tree')) throw new Error('Export dialog did not open');
if (document.querySelector('#import')) throw new Error('Top-level import should be removed');
if (!document.querySelector('#export-project').checked) document.querySelector('#export-project').click();
document.querySelector('#export-selected').click();
for (let i = 0; i < 300 && !document.querySelector('#export-message')?.textContent.includes('保存位置：'); i++) {
  const message = document.querySelector('#export-message')?.textContent || '';
  if (message.includes('导出失败')) throw new Error(message);
  await new Promise(resolve => setTimeout(resolve, 100));
}
const exportMessage = document.querySelector('#export-message')?.textContent || '';
if (!exportMessage.includes('保存位置：')) throw new Error('Batch export timed out: ' + exportMessage);
result.batchExport = {path: exportMessage.split('保存位置：')[1].trim(), succeeded:true, topImportRemoved:true};
