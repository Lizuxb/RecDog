export interface LiveTranscriptStatus { state: string; message: string }
export interface LiveTranscriptEvent {
  sessionId: string;
  text?: string;
  isFinal?: boolean;
  state?: string;
  message?: string;
  error?: string;
  sentenceId?: number | null;
}
export class LiveTranscriptSession {
  constructor(sessionId: string, anchor: { startIndex: number; endIndex: number; [key: string]: unknown }, editor: { replaceLiveTranscript(range: any, previousText: string, nextText: string): void }, onStatus?: (status: LiveTranscriptStatus) => void);
  accept(event: LiveTranscriptEvent): boolean;
  finish(): void;
  retarget(anchor: { startIndex: number; endIndex: number; [key: string]: unknown }): void;
}
