import { ChineseDrama, DramaEpisode } from '../data/chineseDramaLibrary';

export interface SavedDramaProject {
  id: string;
  title: string;
  originalTitle?: string;
  genre: string;
  tone: string;
  synopsis: string;
  episodes: DramaEpisode[];
  createdAt: string;
  updatedAt: string;
  isFavorite?: boolean;
}

const STORAGE_KEY = 'drama_scripts_local_store_v1';
const FAVORITES_KEY = 'drama_scripts_favorites_v1';

export const dramaStorage = {
  /** Lấy danh sách toàn bộ kịch bản đã lưu của người dùng trong LocalStorage */
  getSavedProjects(): SavedDramaProject[] {
    try {
      const data = localStorage.getItem(STORAGE_KEY);
      if (!data) return [];
      return JSON.parse(data);
    } catch {
      return [];
    }
  },

  /** Lưu một kịch bản mới hoặc cập nhật kịch bản đã có */
  saveProject(project: SavedDramaProject): void {
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

  toggleFavorite(dramaId: string): boolean {
    try {
      const favs = this.getFavorites();
      const idx = favs.indexOf(dramaId);
      let isFav = false;
      if (idx >= 0) {
        favs.splice(idx, 1);
        isFav = false;
      } else {
        favs.push(dramaId);
        isFav = true;
      }
      localStorage.setItem(FAVORITES_KEY, JSON.stringify(favs));
      return isFav;
    } catch {
      return false;
    }
  },

  /** Tải xuống file kịch bản (Text hoặc JSON) trực tiếp trên máy người dùng */
  downloadFile(filename: string, content: string, contentType: string = 'text/plain;charset=utf-8'): void {
    const blob = new Blob([content], { type: contentType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  },

  /** Xuất toàn bộ dữ liệu dự án ra JSON để backup */
  exportBackupJson(): void {
    const data = {
      projects: this.getSavedProjects(),
      favorites: this.getFavorites(),
      exportedAt: new Date().toISOString(),
      app: 'AI News & Chinese Drama Studio',
      note: 'Dữ liệu được lưu trữ hoàn toàn trên trình duyệt LocalStorage (không dùng database Railway).'
    };
    this.downloadFile(`kich_ban_phim_trung_quoc_backup_${Date.now()}.json`, JSON.stringify(data, null, 2), 'application/json');
  },

  /** Nhập dữ liệu từ file backup JSON */
  importBackupJson(jsonString: string): { success: boolean; count: number; error?: string } {
    try {
      const parsed = JSON.parse(jsonString);
      if (parsed && Array.isArray(parsed.projects)) {
        const current = this.getSavedProjects();
        const existingIds = new Set(current.map(p => p.id));
        let added = 0;
        for (const proj of parsed.projects) {
          if (proj.id && proj.title) {
            if (!existingIds.has(proj.id)) {
              current.unshift(proj);
              added++;
            }
          }
        }
        localStorage.setItem(STORAGE_KEY, JSON.stringify(current));
        return { success: true, count: added };
      }
      return { success: false, count: 0, error: 'File JSON không đúng cấu trúc kịch bản hợp lệ.' };
    } catch (e) {
      return { success: false, count: 0, error: (e as Error).message };
    }
  }
};
