/** Resolve ownership from stored data, independently of the navigation selection. */
export function documentOwner(workspace, documentId) {
  if (workspace.documents.some(document => document.id === documentId)) return null;
  const folder = workspace.folders.find(folder => folder.documents.some(document => document.id === documentId));
  if (!folder) throw new Error('文档不存在。');
  return folder.id;
}
export function tableDimensions(rows, columns) {
  const values = [Number(rows), Number(columns)];
  if (values.some(value => !Number.isInteger(value) || value < 1 || value > 20)) throw new Error('表格行列数必须是 1 到 20 之间的整数。');
  return values;
}
/** WebView2 can reorder object keys; array ordering still carries document meaning. */
export function canonicalJson(value) {
  return JSON.stringify(value, (_key, item) => item && typeof item === 'object' && !Array.isArray(item) ? Object.fromEntries(Object.keys(item).sort().map(key => [key, item[key]])) : item);
}

export function resourceTime(item) { return Date.parse(item.createdAt || item.updatedAt) || 0; }
