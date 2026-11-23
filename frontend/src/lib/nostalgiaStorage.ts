import { NostalgiaScript } from '../data/nostalgiaScriptLibrary';

export interface SavedNostalgiaProject {
  id: string;
  script: NostalgiaScript;
  createdAt: string;
  updatedAt: string;
  isFavorite?: boolean;
}

const STORAGE_KEY = 'nostalgia_scripts_local_store_v1';
const FAVORITES_KEY = 'nostalgia_scripts_favorites_v1';

export const nostalgiaStorage = {
  /** Lấy danh sách kịch bản tùy chỉnh đã lưu trong LocalStorage */
  getSavedProjects(): SavedNostalgiaProject[] {
    try {
      const data = localStorage.getItem(STORAGE_KEY);
      if (!data) return [];
      return JSON.parse(data);
    } catch {
      return [];
    }
  },

  /** Lưu kịch bản mới hoặc cập nhật kịch bản đã có */
  saveProject(project: SavedNostalgiaProject): void {
    try {
      const projects = this.getSavedProjects();
      const idx = projects.findIndex(p => p.id === project.id);
      project.updatedAt = new Date().toISOString();
      if (idx >= 0) {
        projects[idx] = project;
      } else {
        projects.unshift(project);
      }
      localStorage.setItem(STORAGE_KEY, JSON.stringify(projects));
    } catch (e) {
      console.error('Không thể lưu kịch bản vào LocalStorage:', e);
    }
  },

  /** Xóa một kịch bản khỏi LocalStorage */
  deleteProject(id: string): void {
    try {
      const projects = this.getSavedProjects().filter(p => p.id !== id);
      localStorage.setItem(STORAGE_KEY, JSON.stringify(projects));
    } catch (e) {
      console.error('Lỗi khi xóa kịch bản:', e);
    }
  },

  /** Quản lý danh sách ID kịch bản mẫu yêu thích */
  getFavorites(): string[] {
    try {
      const data = localStorage.getItem(FAVORITES_KEY);
      return data ? JSON.parse(data) : [];
    } catch {
      return [];
    }
  },

  toggleFavorite(scriptId: string): boolean {
    try {
      const favs = this.getFavorites();
      const idx = favs.indexOf(scriptId);
      let isFav = false;
      if (idx >= 0) {
        favs.splice(idx, 1);
        isFav = false;
      } else {
        favs.push(scriptId);
        isFav = true;
      }
      localStorage.setItem(FAVORITES_KEY, JSON.stringify(favs));
      return isFav;
    } catch {
      return false;
    }
  },

  /** Tải xuống file văn bản (TXT, JSON) trực tiếp trên trình duyệt */
  downloadFile(filename: string, content: string, mimeType = 'text/plain;charset=utf-8'): void {
    const blob = new Blob([content], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  },

  /** Xuất toàn bộ kịch bản ra file JSON backup */
  exportBackupJson(): void {
    const data = {
      version: '1.0',
      exportedAt: new Date().toISOString(),
      projects: this.getSavedProjects(),
      favorites: this.getFavorites(),
    };
    this.downloadFile(
      `kich-ban-ky-uc-tuoi-tho-backup-${new Date().toISOString().slice(0, 10)}.json`,
      JSON.stringify(data, null, 2),
      'application/json;charset=utf-8'
    );
  },

  /** Nhập lại file JSON backup */
  importBackupJson(jsonString: string): { success: boolean; count: number; message: string } {
    try {
      const data = JSON.parse(jsonString);
      if (!data || !Array.isArray(data.projects)) {
        return { success: false, count: 0, message: 'File backup không hợp lệ!' };
      }
      const existing = this.getSavedProjects();
      const existingMap = new Map(existing.map(p => [p.id, p]));
      let newCount = 0;

      for (const p of data.projects) {
        if (p.id && p.script) {
          existingMap.set(p.id, p);
          newCount++;
        }
      }

      const merged = Array.from(existingMap.values());
      localStorage.setItem(STORAGE_KEY, JSON.stringify(merged));

      if (Array.isArray(data.favorites)) {
        const curFavs = new Set([...this.getFavorites(), ...data.favorites]);
        localStorage.setItem(FAVORITES_KEY, JSON.stringify(Array.from(curFavs)));
      }

      return { success: true, count: newCount, message: `Đã nạp thành công ${newCount} kịch bản!` };
    } catch (e: any) {
      return { success: false, count: 0, message: `Lỗi đọc file: ${e.message}` };
    }
  },
};
