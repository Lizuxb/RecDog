/** Tracks persistence acknowledgements independently of continued editing. */
export class DocumentState {
  id = '';
  revision = 0;
  savedRevision = 0;
  load(document) {
    this.id = document.id;
    this.revision = 0;
    this.savedRevision = 0;
  }
  changed() { this.revision += 1; }
  capture() { return { id: this.id, revision: this.revision }; }
  persisted(snapshot) {
    if (snapshot.id === this.id) this.savedRevision = Math.max(this.savedRevision, snapshot.revision);
  }
  get dirty() { return this.revision > this.savedRevision; }
}
