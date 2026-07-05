import { Injectable } from '@angular/core';

/**
 * Dev-only writer for help content, via the File System Access API.
 *
 * In local development the files `ng serve` serves under public/help ARE the working tree, so a
 * SuperUser can edit help in the running app and the save lands directly in the repo — then it's
 * committed and pushed like any other change, and travels with publish. No backend, no dev-server
 * middleware, no second process. On staging/prod the build is static and there is no working tree to
 * write, so the pencil never appears (see HelpManifestService.canEdit).
 *
 * The author grants access to the public/help folder once per session (Chromium file picker); writes
 * go to {component}/{topic}.html beneath it. Reads never touch this service.
 */
@Injectable({ providedIn: 'root' })
export class HelpFsService {
  // FileSystemDirectoryHandle — loosely typed; the picker/permission APIs aren't in every TS DOM lib.
  private dir: any = null;

  /** True only where the browser can pick a directory (Chromium). Gates the edit affordance. */
  get supported(): boolean {
    return typeof (globalThis as unknown as { showDirectoryPicker?: unknown }).showDirectoryPicker === 'function';
  }

  async write(component: string, topic: string, html: string): Promise<void> {
    const root = await this.ensureHelpDir();
    const componentDir = await root.getDirectoryHandle(component, { create: true });
    const fileHandle = await componentDir.getFileHandle(`${topic}.html`, { create: true });
    const writable = await fileHandle.createWritable();
    await writable.write(html.endsWith('\n') ? html : `${html}\n`);
    await writable.close();
  }

  /** Resolve — and remember for the session — a handle to the public/help directory. */
  private async ensureHelpDir(): Promise<any> {
    if (this.dir && (await this.hasWritePermission(this.dir))) return this.dir;

    const picker = (globalThis as unknown as { showDirectoryPicker: (o: unknown) => Promise<any> }).showDirectoryPicker;
    const picked = await picker({ id: 'tsic-help', mode: 'readwrite' });

    // Be forgiving about the selection: accept the `help` folder itself, or a parent that contains one
    // (e.g. the author picks public/ or tsic-app/public by mistake).
    const resolved = picked.name === 'help' ? picked : await this.findHelpChild(picked);
    if (!resolved) {
      throw new Error('Choose the public/help folder (or a parent that contains it).');
    }
    if (!(await this.hasWritePermission(resolved))) {
      throw new Error('Write permission was not granted for that folder.');
    }
    this.dir = resolved;
    return resolved;
  }

  private async findHelpChild(handle: any): Promise<any | null> {
    try {
      return await handle.getDirectoryHandle('help');
    } catch {
      return null;
    }
  }

  private async hasWritePermission(handle: any): Promise<boolean> {
    const opts = { mode: 'readwrite' };
    if ((await handle.queryPermission?.(opts)) === 'granted') return true;
    return (await handle.requestPermission?.(opts)) === 'granted';
  }
}
