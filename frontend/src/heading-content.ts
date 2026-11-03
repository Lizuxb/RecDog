import type { DocumentContent } from './bridge';

/** Repair legacy level-only text; preserve existing native title groups and ordinary formatting. */
export function normalizeHeadingContent(content: DocumentContent): DocumentContent {
  const copy = structuredClone(content);
  const levels = new Set(['first', 'second', 'third', 'fourth', 'fifth', 'sixth']);
  function normalize(elements: unknown[]): unknown[] {
    let level: unknown;
    let titleId: string | undefined;
    return elements.flatMap(value => {
      if (!value || typeof value !== 'object') return [value];
      const element = value as Record<string, any>;
      if (Array.isArray(element.valueList)) element.valueList = normalize(element.valueList);
      for (const row of element.trList || []) for (const cell of row.tdList || []) {
        if (Array.isArray(cell.value)) cell.value = normalize(cell.value);
      }
      if (levels.has(element.level) && !element.titleId && (!element.type || element.type === 'text') && typeof element.value === 'string') {
        if (level !== element.level) titleId = undefined;
        level = element.level;
        return element.value.split(/(\r\n?|\n|\u200b)/).filter(Boolean).map((part: string) => {
          if (/^[\r\n\u200b]+$/.test(part)) {
            titleId = undefined;
            const { level: _level, ...separator } = element;
            return { ...separator, value: part };
          }
          titleId ||= crypto.randomUUID();
          return { ...element, value: part, titleId };
        });
      }
      level = undefined; titleId = undefined;
      return [element];
    });
  }
  copy.main = normalize(copy.main);
  return copy;
}
