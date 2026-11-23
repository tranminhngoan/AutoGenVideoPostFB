import React, { useState, useEffect, useMemo } from 'react';
import {
  Search,
  Copy,
  Check,
  Download,
  Trash2,
  Clapperboard,
  Clock,
  Volume2,
  Eye,
  Wand2,
  Heart,
  FileText,
  Bookmark,
  Film,
  Camera,
  Layers,
  X,
  RotateCcw,
} from 'lucide-react';
import {
  Badge,
  Button,
  Card,
  Field,
  PageHeader,
  Select,
  Textarea,
  cn,
} from '../components/ui';
import { useToast } from '../components/feedback';
import { navigate } from '../lib/router';
import {
  ALL_NOSTALGIA_SCRIPTS,
  NOSTALGIA_CATEGORIES,
  NostalgiaScript,
  getNostalgiaScriptById,
} from '../data/nostalgiaScriptLibrary';
import {
  convertNostalgiaScenesToStudioShots,
  extractNostalgiaVoiceover,
  formatNostalgiaProductionScript,
  extractNostalgiaAIPrompts,
  generateCustomChildhoodScript,
} from '../lib/nostalgiaScriptEngine';
import {
  nostalgiaStorage,
  SavedNostalgiaProject,
} from '../lib/nostalgiaStorage';

type ActiveTab = 'library' | 'custom' | 'saved';

export function DramaScriptsPage() {
  const toast = useToast();
  const [activeTab, setActiveTab] = useState<ActiveTab>('library');

  // Search & Filter state
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedCategory, setSelectedCategory] = useState<string>('all');
  const [selectedRegion, setSelectedRegion] = useState<string>('all');
  const [selectedMood, setSelectedMood] = useState<string>('all');
  const [selectedEra, setSelectedEra] = useState<string>('all');
  const [favorites, setFavorites] = useState<string[]>(() => nostalgiaStorage.getFavorites());

  // Selected script for Detail Modal
  const [selectedScript, setSelectedScript] = useState<NostalgiaScript | null>(null);

  // Custom Memory Builder state
  const [customMemoryText, setCustomMemoryText] = useState('');
  const [customRegion, setCustomRegion] = useState<'Bắc Bộ' | 'Miền Trung' | 'Miền Tây' | 'Chung'>('Bắc Bộ');
  const [customEra, setCustomEra] = useState<'8x' | '9x'>('8x');
  const [customMood, setCustomMood] = useState<'Nghẹn ngào rớt nước mắt' | 'Da diết nhớ cha mẹ' | 'Ấm áp hoài niệm' | 'Xót xa thương quê' | 'Hóm hỉnh ngây ngô'>('Nghẹn ngào rớt nước mắt');
  const [generatedCustomScript, setGeneratedCustomScript] = useState<NostalgiaScript | null>(null);

  // Saved projects
  const [savedProjects, setSavedProjects] = useState<SavedNostalgiaProject[]>([]);
  const [copiedKey, setCopiedKey] = useState<string | null>(null);

  // Load saved projects on mount
  useEffect(() => {
    setSavedProjects(nostalgiaStorage.getSavedProjects());
  }, []);

  // Filtered scripts in Library
  const filteredScripts = useMemo(() => {
    return ALL_NOSTALGIA_SCRIPTS.filter((script) => {
      if (selectedCategory !== 'all' && script.category !== selectedCategory) {
        return false;
      }
      if (selectedRegion !== 'all' && script.region !== selectedRegion) {
        return false;
      }
      if (selectedMood !== 'all' && script.mood !== selectedMood) {
        return false;
      }
      if (selectedEra !== 'all' && script.era !== selectedEra) {
        return false;
      }
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase().trim();
        const inTitle = script.title.toLowerCase().includes(q);
        const inCode = script.code.toLowerCase().includes(q);
        const inSynopsis = script.synopsis.toLowerCase().includes(q);
        const inHook = script.hook.toLowerCase().includes(q);
        const inScenes = script.scenes.some(
          (s) => s.visualVi.toLowerCase().includes(q) || s.voiceover.toLowerCase().includes(q)
        );
        if (!inTitle && !inCode && !inSynopsis && !inHook && !inScenes) {
          return false;
        }
      }
      return true;
    });
  }, [searchQuery, selectedCategory, selectedRegion, selectedMood, selectedEra]);

  // Copy helper with feedback
  const handleCopy = async (text: string, key: string, label: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopiedKey(key);
      toast.success(`Đã sao chép ${label}!`);
      setTimeout(() => setCopiedKey(null), 2500);
    } catch {
      toast.error('Không thể sao chép vào bộ nhớ đệm');
    }
  };

  // Toggle favorite
  const handleToggleFavorite = (scriptId: string) => {
    const isFav = nostalgiaStorage.toggleFavorite(scriptId);
    setFavorites(nostalgiaStorage.getFavorites());
    toast.success(isFav ? 'Đã lưu vào yêu thích' : 'Đã bỏ yêu thích');
  };

  // 1-Click transfer to Studio Video AI
  const handleSendToStudio = (script: NostalgiaScript) => {
    try {
      const fullScript = formatNostalgiaProductionScript(script);
      const convertedShots = convertNostalgiaScenesToStudioShots(script.scenes);

      sessionStorage.setItem('studio_prefill_script', fullScript);
      sessionStorage.setItem('studio_prefill_title', `${script.title} (${script.code})`);
      sessionStorage.setItem('studio_prefill_duration', String(script.targetDurationSeconds || 120));
      sessionStorage.setItem('studio_prefill_shots', JSON.stringify(convertedShots));

      toast.success(`Đã nạp 5 phân cảnh của "${script.title}" vào Studio!`);
      navigate('/studio/new');
    } catch (e: any) {
      toast.error('Lỗi khi nạp kịch bản vào Studio: ' + (e?.message || 'Không xác định'));
    }
  };

  // Generate Custom Script
  const handleCreateCustomScript = () => {
    if (!customMemoryText.trim()) {
      toast.info('Vui lòng nhập một kỷ niệm tuổi thơ của bạn (ví dụ: bữa cơm chiều mưa, xe đạp đứt xích...)');
      return;
    }
    const script = generateCustomChildhoodScript({
      userMemory: customMemoryText,
      region: customRegion,
      era: customEra,
      mood: customMood,
    });
    setGeneratedCustomScript(script);
    toast.success('Đã sinh kịch bản 5 phân cảnh chuẩn 2 phút!');
  };

  // Save generated custom script to LocalStorage
  const handleSaveCustomProject = (script: NostalgiaScript) => {
    const newProj: SavedNostalgiaProject = {
      id: script.id,
      script,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      isFavorite: false,
    };
    nostalgiaStorage.saveProject(newProj);
    setSavedProjects(nostalgiaStorage.getSavedProjects());
    toast.success(`Đã lưu "${script.title}" vào mục cá nhân!`);
  };

  const isFiltered =
    Boolean(searchQuery) ||
    selectedCategory !== 'all' ||
    selectedRegion !== 'all' ||
    selectedMood !== 'all' ||
    selectedEra !== 'all';

  const resetFilters = () => {
    setSearchQuery('');
    setSelectedCategory('all');
    setSelectedRegion('all');
    setSelectedMood('all');
    setSelectedEra('all');
  };

  return (
    <div className="space-y-4 sm:space-y-6 pb-20">
      {/* Page Header */}
      <PageHeader
        title="Lên kịch bản"
        description="Kho kịch bản hoài niệm tuổi thơ 8x 9x làng quê Việt Nam. Tái hiện chân thực mái nhà tranh, vách đất, bữa cơm độn sắn khoai, áo may ô cháo lòng, đôi dép tổ ong mòn rách. Tối ưu prompt chi tiết cho AI Video."
        actions={
          <div className="flex items-center gap-2 w-full sm:w-auto">
            <Button
              variant="secondary"
              size="sm"
              icon={<Download className="h-4 w-4" />}
              onClick={() => nostalgiaStorage.exportBackupJson()}
              className="flex-1 sm:flex-initial text-xs"
            >
              Sao lưu
            </Button>
            <Button
              variant="primary"
              size="sm"
              icon={<Clapperboard className="h-4 w-4" />}
              onClick={() => navigate('/studio/new')}
              className="flex-1 sm:flex-initial text-xs font-medium"
            >
              Vào Studio
            </Button>
          </div>
        }
      />

      {/* Gentle & Calming Banner */}
      <div className="rounded-xl border border-line bg-subtle p-3.5 sm:p-4 text-xs sm:text-sm text-fg-muted flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3">
        <div className="space-y-0.5">
          <div className="flex items-center gap-2 text-fg font-medium">
            <span className="text-base">🌾</span>
            <span>"Bao nhiêu của cải phố thị cũng chẳng thể mua lại được một chiều mưa bên mẹ..."</span>
          </div>
          <p className="text-xs text-fg-subtle">
            300 kịch bản chuẩn 2 phút (5 phân cảnh). Đầy đủ mô tả tiếng Việt và Master Prompt tiếng Anh cho Kling AI, Midjourney, Hailuo.
          </p>
        </div>
        <div className="flex items-center gap-2 shrink-0 text-xs">
          <Badge tone="neutral" className="text-[11px] font-mono">
            300 Kịch bản
          </Badge>
          <Badge tone="neutral" className="text-[11px]">
            Lưu offline
          </Badge>
        </div>
      </div>

      {/* Main Tabs Navigation (Touch Friendly on Mobile) */}
      <div className="-mx-4 sm:mx-0 px-4 sm:px-0 flex gap-2 border-b border-line overflow-x-auto no-scrollbar pb-1 text-xs sm:text-sm font-medium">
        <button
          type="button"
          onClick={() => setActiveTab('library')}
          className={cn(
            'flex items-center gap-2 px-3.5 py-2.5 border-b-2 transition-colors whitespace-nowrap',
            activeTab === 'library'
              ? 'border-fg text-fg font-semibold'
              : 'border-transparent text-fg-muted hover:text-fg hover:border-line'
          )}
        >
          <Layers className="h-4 w-4" />
          <span>Thư viện 300 kịch bản</span>
          <span className="rounded-full bg-muted px-2 py-0.5 text-[11px] text-fg-muted">300</span>
        </button>

        <button
          type="button"
          onClick={() => setActiveTab('custom')}
          className={cn(
            'flex items-center gap-2 px-3.5 py-2.5 border-b-2 transition-colors whitespace-nowrap',
            activeTab === 'custom'
              ? 'border-fg text-fg font-semibold'
              : 'border-transparent text-fg-muted hover:text-fg hover:border-line'
          )}
        >
          <Wand2 className="h-4 w-4" />
          <span>Tạo theo ký ức riêng</span>
        </button>

        <button
          type="button"
          onClick={() => setActiveTab('saved')}
          className={cn(
            'flex items-center gap-2 px-3.5 py-2.5 border-b-2 transition-colors whitespace-nowrap',
            activeTab === 'saved'
              ? 'border-fg text-fg font-semibold'
              : 'border-transparent text-fg-muted hover:text-fg hover:border-line'
          )}
        >
          <Bookmark className="h-4 w-4" />
          <span>Đã lưu & Yêu thích</span>
          <span className="rounded-full bg-muted px-2 py-0.5 text-[11px] text-fg-muted">
            {savedProjects.length + favorites.length}
          </span>
        </button>
      </div>

      {/* TAB 1: THƯ VIỆN 300 KỊCH BẢN */}
      {activeTab === 'library' && (
        <div className="space-y-4 sm:space-y-5">
          {/* Category Chips Bar (Mobile friendly horizontal scroll) */}
          <div className="space-y-2">
            <div className="flex items-center justify-between text-xs text-fg-subtle px-0.5">
              <span>Chủ đề (50 kịch bản / chủ đề):</span>
              <span className="text-[11px] text-fg-muted sm:hidden">Vuốt sang để xem thêm →</span>
            </div>

            <div className="-mx-4 sm:mx-0 px-4 sm:px-0 flex gap-2 overflow-x-auto no-scrollbar pb-1">
              <button
                type="button"
                onClick={() => setSelectedCategory('all')}
                className={cn(
                  'px-3 py-2 rounded-lg border text-xs font-medium whitespace-nowrap transition-all flex items-center gap-1.5 shrink-0',
                  selectedCategory === 'all'
                    ? 'border-fg bg-surface text-fg shadow-sm'
                    : 'border-line bg-surface/50 text-fg-muted hover:text-fg hover:bg-surface'
                )}
              >
                <span>✨</span>
                <span>Tất cả (300)</span>
              </button>

              {NOSTALGIA_CATEGORIES.map((cat) => (
                <button
                  key={cat.id}
                  type="button"
                  onClick={() => setSelectedCategory(cat.id)}
                  className={cn(
                    'px-3 py-2 rounded-lg border text-xs font-medium whitespace-nowrap transition-all flex items-center gap-1.5 shrink-0',
                    selectedCategory === cat.id
                      ? 'border-fg bg-surface text-fg shadow-sm'
                      : 'border-line bg-surface/50 text-fg-muted hover:text-fg hover:bg-surface'
                  )}
                >
                  <span>{cat.icon}</span>
                  <span>{cat.name}</span>
                  <span className="text-[10px] text-fg-subtle font-mono">(50)</span>
                </button>
              ))}
            </div>
          </div>

          {/* Search & Mobile Responsive Filters */}
          <div className="rounded-xl border border-line bg-surface p-3 sm:p-4 space-y-3">
            {/* Search Input */}
            <div className="relative">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-fg-subtle" />
              <input
                type="text"
                placeholder="Tìm kịch bản: cơm độn, kem mút, diều, mẹ vá áo, dép tổ ong, tắm sông..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="w-full pl-9 pr-8 py-2.5 text-xs sm:text-sm rounded-lg border border-line bg-canvas text-fg placeholder:text-fg-subtle focus:border-fg/40 focus:outline-none transition-colors"
              />
              {searchQuery && (
                <button
                  type="button"
                  onClick={() => setSearchQuery('')}
                  className="absolute right-2.5 top-1/2 -translate-y-1/2 p-1 text-fg-subtle hover:text-fg"
                >
                  <X className="h-3.5 w-3.5" />
                </button>
              )}
            </div>

            {/* Filter Dropdowns Grid */}
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-2 pt-0.5">
              <select
                value={selectedRegion}
                onChange={(e) => setSelectedRegion(e.target.value)}
                className="rounded-lg border border-line bg-canvas px-2.5 py-2 text-xs text-fg focus:outline-none"
              >
                <option value="all">Mọi vùng miền</option>
                <option value="Bắc Bộ">Làng quê Bắc Bộ</option>
                <option value="Miền Trung">Miền Trung nắng gió</option>
                <option value="Miền Tây">Miền Tây sông nước</option>
              </select>

              <select
                value={selectedMood}
                onChange={(e) => setSelectedMood(e.target.value)}
                className="rounded-lg border border-line bg-canvas px-2.5 py-2 text-xs text-fg focus:outline-none"
              >
                <option value="all">Mọi cảm xúc</option>
                <option value="Nghẹn ngào rớt nước mắt">Nghẹn ngào rớt nước mắt</option>
                <option value="Da diết nhớ cha mẹ">Da diết nhớ cha mẹ</option>
                <option value="Ấm áp hoài niệm">Ấm áp hoài niệm</option>
                <option value="Xót xa thương quê">Xót xa thương quê</option>
                <option value="Hóm hỉnh ngây ngô">Hóm hỉnh ngây ngô</option>
              </select>

              <select
                value={selectedEra}
                onChange={(e) => setSelectedEra(e.target.value)}
                className="rounded-lg border border-line bg-canvas px-2.5 py-2 text-xs text-fg focus:outline-none"
              >
                <option value="all">Thời kỳ 8x & 9x</option>
                <option value="8x">Thế hệ 8x (Bao cấp)</option>
                <option value="9x">Thế hệ 9x (Đổi mới)</option>
              </select>

              {isFiltered ? (
                <button
                  type="button"
                  onClick={resetFilters}
                  className="rounded-lg border border-line bg-muted/60 hover:bg-muted px-2.5 py-2 text-xs text-fg flex items-center justify-center gap-1.5 transition-colors"
                >
                  <RotateCcw className="h-3 w-3" />
                  <span>Đặt lại</span>
                </button>
              ) : (
                <div className="hidden sm:flex items-center justify-end text-[11px] text-fg-subtle px-1">
                  {filteredScripts.length} kết quả
                </div>
              )}
            </div>
          </div>

          {/* Results Summary */}
          <div className="flex items-center justify-between text-xs text-fg-subtle px-1">
            <span>
              Đang hiển thị <strong>{filteredScripts.length}</strong> / 300 kịch bản
            </span>
            <span className="text-[11px] hidden sm:inline">
              Nhấn "Chi tiết" để xem 5 phân cảnh hoặc "Dựng Video" để nạp vào Studio
            </span>
          </div>

          {/* Scripts Grid (Responsive) */}
          {filteredScripts.length === 0 ? (
            <Card className="p-8 sm:p-12 text-center space-y-3">
              <div className="text-3xl sm:text-4xl">🌾</div>
              <h4 className="font-semibold text-fg text-sm sm:text-base">Không tìm thấy kịch bản phù hợp</h4>
              <p className="text-xs text-fg-subtle max-w-sm mx-auto">
                Không có kịch bản nào khớp với bộ lọc hiện tại. Hãy thử tìm từ khóa khác hoặc đặt lại bộ lọc.
              </p>
              <Button variant="secondary" size="sm" onClick={resetFilters} className="text-xs">
                Xem lại toàn bộ 300 kịch bản
              </Button>
            </Card>
          ) : (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3 sm:gap-4">
              {filteredScripts.map((script) => {
                const isFav = favorites.includes(script.id);
                return (
                  <Card
                    key={script.id}
                    className="flex flex-col justify-between p-3.5 sm:p-4 space-y-3 hover:border-line-strong transition-all group"
                  >
                    <div className="space-y-2">
                      {/* Card Header */}
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex flex-wrap items-center gap-1.5">
                          <span className="font-mono text-[11px] font-medium text-fg-muted bg-muted px-1.5 py-0.5 rounded">
                            {script.code}
                          </span>
                          <span className="text-[11px] text-fg-subtle">
                            {script.region} • {script.era}
                          </span>
                        </div>
                        <button
                          type="button"
                          onClick={() => handleToggleFavorite(script.id)}
                          className={cn(
                            'p-1.5 rounded-md hover:bg-muted transition-colors',
                            isFav ? 'text-fg' : 'text-fg-subtle hover:text-fg'
                          )}
                          title={isFav ? 'Bỏ yêu thích' : 'Lưu yêu thích'}
                        >
                          <Heart className={cn('h-3.5 w-3.5', isFav && 'fill-current')} />
                        </button>
                      </div>

                      {/* Title */}
                      <h4 className="font-semibold text-fg text-sm leading-snug line-clamp-2">
                        {script.title}
                      </h4>

                      {/* Hook Quote */}
                      <p className="text-xs text-fg-muted italic border-l-2 border-line pl-2.5 py-0.5 line-clamp-2 leading-relaxed">
                        "{script.hook}"
                      </p>

                      {/* Synopsis */}
                      <p className="text-xs text-fg-subtle line-clamp-2 leading-relaxed">
                        {script.synopsis}
                      </p>

                      {/* Mood Tag */}
                      <div className="pt-1 flex items-center justify-between text-[11px] text-fg-subtle">
                        <span>🎭 {script.mood}</span>
                        <span>⏱️ ~2p (5 cảnh)</span>
                      </div>
                    </div>

                    {/* Card Actions (Mobile Ergonomic) */}
                    <div className="pt-2.5 border-t border-line flex items-center justify-between gap-2">
                      <Button
                        variant="ghost"
                        size="sm"
                        icon={<Eye className="h-3.5 w-3.5" />}
                        onClick={() => setSelectedScript(script)}
                        className="text-xs text-fg-muted hover:text-fg px-2.5"
                      >
                        Chi tiết
                      </Button>

                      <div className="flex items-center gap-1.5">
                        <Button
                          variant="secondary"
                          size="sm"
                          icon={copiedKey === `prompt_${script.id}` ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
                          onClick={() => handleCopy(extractNostalgiaAIPrompts(script), `prompt_${script.id}`, 'Prompt AI')}
                          title="Copy Master Prompt AI Video"
                          className="text-xs px-2.5"
                        >
                          Prompt AI
                        </Button>

                        <Button
                          variant="primary"
                          size="sm"
                          icon={<Clapperboard className="h-3.5 w-3.5" />}
                          onClick={() => handleSendToStudio(script)}
                          title="Nạp vào Studio Video AI"
                          className="text-xs font-medium px-2.5"
                        >
                          Dựng Video
                        </Button>
                      </div>
                    </div>
                  </Card>
                );
              })}
            </div>
          )}
        </div>
      )}

      {/* TAB 2: TẠO KỊCH BẢN KÝ ỨC RIÊNG */}
      {activeTab === 'custom' && (
        <div className="space-y-5 max-w-3xl mx-auto">
          <Card className="p-4 sm:p-6 space-y-4">
            <div className="space-y-1">
              <h3 className="font-semibold text-fg text-base flex items-center gap-2">
                <Wand2 className="h-4 w-4" />
                <span>Lên kịch bản từ ký ức của bạn</span>
              </h3>
              <p className="text-xs text-fg-subtle leading-relaxed">
                Nhập mẩu chuyện thời 8x 9x của bạn (bữa cơm chiều mưa, một lần tắm sông, chiếc xe đạp cũ...). Hệ thống sẽ phân rã thành 5 phân cảnh chuẩn 2 phút kèm Master Prompt tiếng Anh cho AI Video.
              </p>
            </div>

            <div className="space-y-3.5">
              <Field label="Kể lại mẩu ký ức của bạn:">
                <Textarea
                  rows={4}
                  value={customMemoryText}
                  onChange={(e) => setCustomMemoryText(e.target.value)}
                  placeholder="Ví dụ: Nhớ chiều mưa năm 1996 ở quê, bố đạp xe đèo tôi đi thi học sinh giỏi huyện. Đường đất trơn trượt bị đứt xích, hai bố con dắt bộ lội bùn, bố cởi áo mưa che cho tôi còn mình ướt sũng..."
                  className="text-xs sm:text-sm"
                />
              </Field>

              <div className="grid grid-cols-1 sm:grid-cols-3 gap-2.5">
                <Field label="Vùng miền:">
                  <Select
                    value={customRegion}
                    onChange={(e) => setCustomRegion(e.target.value as any)}
                  >
                    <option value="Bắc Bộ">Làng quê Bắc Bộ</option>
                    <option value="Miền Trung">Miền Trung nắng gió</option>
                    <option value="Miền Tây">Miền Tây sông nước</option>
                    <option value="Chung">Làng quê chung</option>
                  </Select>
                </Field>

                <Field label="Thời kỳ:">
                  <Select
                    value={customEra}
                    onChange={(e) => setCustomEra(e.target.value as any)}
                  >
                    <option value="8x">Thế hệ 8x (Bao cấp)</option>
                    <option value="9x">Thế hệ 9x (Đổi mới)</option>
                  </Select>
                </Field>

                <Field label="Tông cảm xúc:">
                  <Select
                    value={customMood}
                    onChange={(e) => setCustomMood(e.target.value as any)}
                  >
                    <option value="Nghẹn ngào rớt nước mắt">Nghẹn ngào rớt nước mắt</option>
                    <option value="Da diết nhớ cha mẹ">Da diết nhớ cha mẹ</option>
                    <option value="Ấm áp hoài niệm">Ấm áp hoài niệm</option>
                    <option value="Xót xa thương quê">Xót xa thương quê</option>
                    <option value="Hóm hỉnh ngây ngô">Hóm hỉnh ngây ngô</option>
                  </Select>
                </Field>
              </div>

              <div className="pt-2 flex justify-end">
                <Button
                  variant="primary"
                  icon={<Wand2 className="h-4 w-4" />}
                  onClick={handleCreateCustomScript}
                  className="w-full sm:w-auto text-xs font-medium"
                >
                  Sinh kịch bản 2 phút
                </Button>
              </div>
            </div>
          </Card>

          {/* Generated Custom Script Preview */}
          {generatedCustomScript && (
            <Card className="p-4 sm:p-5 space-y-4">
              <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 border-b border-line pb-3">
                <div className="space-y-0.5">
                  <div className="flex items-center gap-2">
                    <span className="font-mono text-xs font-semibold text-fg-muted bg-muted px-2 py-0.5 rounded">
                      {generatedCustomScript.code}
                    </span>
                    <Badge tone="neutral" className="text-[11px]">
                      5 phân cảnh (~2 phút)
                    </Badge>
                  </div>
                  <h4 className="font-bold text-fg text-sm sm:text-base mt-1">
                    {generatedCustomScript.title}
                  </h4>
                </div>

                <div className="flex items-center gap-2 w-full sm:w-auto">
                  <Button
                    variant="secondary"
                    size="sm"
                    icon={<Bookmark className="h-3.5 w-3.5" />}
                    onClick={() => handleSaveCustomProject(generatedCustomScript)}
                    className="flex-1 sm:flex-initial text-xs"
                  >
                    Lưu
                  </Button>
                  <Button
                    variant="primary"
                    size="sm"
                    icon={<Clapperboard className="h-3.5 w-3.5" />}
                    onClick={() => handleSendToStudio(generatedCustomScript)}
                    className="flex-1 sm:flex-initial text-xs font-medium"
                  >
                    Dựng Video
                  </Button>
                </div>
              </div>

              <div className="space-y-3">
                <div className="p-2.5 rounded-lg bg-subtle border border-line text-xs text-fg-muted italic">
                  <strong>Hook (0-5s):</strong> "{generatedCustomScript.hook}"
                </div>

                <div className="space-y-2.5 pt-1">
                  <h5 className="font-semibold text-xs text-fg uppercase tracking-wider">
                    5 Phân cảnh chi tiết:
                  </h5>
                  {generatedCustomScript.scenes.map((scene) => (
                    <div
                      key={scene.sceneNumber}
                      className="rounded-lg border border-line p-3 space-y-2 bg-canvas text-xs"
                    >
                      <div className="flex items-center justify-between font-medium text-fg">
                        <span>
                          Cảnh {scene.sceneNumber}: {scene.title}
                        </span>
                        <span className="text-fg-subtle font-mono text-[11px]">{scene.durationSeconds}s</span>
                      </div>

                      <div className="space-y-0.5">
                        <span className="text-fg-subtle font-medium">🎥 Hình ảnh:</span>
                        <p className="text-fg-muted pl-2 border-l border-line leading-relaxed">
                          {scene.visualVi}
                        </p>
                      </div>

                      <div className="space-y-0.5 bg-subtle p-2 rounded border border-line/50">
                        <span className="text-fg font-mono text-[10px] font-semibold">
                          🤖 AI Master Prompt:
                        </span>
                        <p className="text-fg-muted font-mono text-[11px] leading-relaxed select-all">
                          {scene.aiPromptEn}
                        </p>
                      </div>

                      <div className="space-y-0.5">
                        <span className="text-fg-subtle font-medium">🎙️ Lời bình:</span>
                        <p className="text-fg italic pl-2 border-l border-line leading-relaxed">
                          "{scene.voiceover}"
                        </p>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </Card>
          )}
        </div>
      )}

      {/* TAB 3: ĐÃ LƯU & YÊU THÍCH */}
      {activeTab === 'saved' && (
        <div className="space-y-5">
          {/* Favorites List */}
          <div className="space-y-3">
            <h3 className="font-semibold text-fg text-sm flex items-center gap-2">
              <Heart className="h-4 w-4 fill-current text-fg" />
              <span>Kịch bản yêu thích ({favorites.length})</span>
            </h3>

            {favorites.length === 0 ? (
              <Card className="p-6 text-center text-xs text-fg-subtle">
                Chưa có kịch bản yêu thích. Nhấn vào biểu tượng trái tim ở kịch bản trong thư viện để lưu nhanh.
              </Card>
            ) : (
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                {favorites.map((favId) => {
                  const script = getNostalgiaScriptById(favId);
                  if (!script) return null;
                  return (
                    <Card key={script.id} className="p-3.5 space-y-2 flex flex-col justify-between">
                      <div className="space-y-1.5">
                        <div className="flex items-center justify-between">
                          <span className="font-mono text-[11px] font-medium text-fg-muted bg-muted px-1.5 py-0.5 rounded">
                            {script.code}
                          </span>
                          <button
                            type="button"
                            onClick={() => handleToggleFavorite(script.id)}
                            className="p-1 text-fg hover:text-fg-subtle"
                          >
                            <Heart className="h-3.5 w-3.5 fill-current" />
                          </button>
                        </div>
                        <h4 className="font-semibold text-fg text-xs line-clamp-1">
                          {script.title}
                        </h4>
                        <p className="text-[11px] text-fg-subtle line-clamp-2">
                          {script.hook}
                        </p>
                      </div>
                      <div className="pt-2 border-t border-line flex items-center justify-between gap-2">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setSelectedScript(script)}
                          className="text-xs"
                        >
                          Chi tiết
                        </Button>
                        <Button
                          variant="primary"
                          size="sm"
                          icon={<Clapperboard className="h-3.5 w-3.5" />}
                          onClick={() => handleSendToStudio(script)}
                          className="text-xs font-medium"
                        >
                          Dựng Video
                        </Button>
                      </div>
                    </Card>
                  );
                })}
              </div>
            )}
          </div>

          {/* User Custom Saved Projects */}
          <div className="space-y-3 pt-4 border-t border-line">
            <div className="flex items-center justify-between">
              <h3 className="font-semibold text-fg text-sm flex items-center gap-2">
                <Bookmark className="h-4 w-4" />
                <span>Kịch bản tự tạo ({savedProjects.length})</span>
              </h3>
              {savedProjects.length > 0 && (
                <Button
                  variant="secondary"
                  size="sm"
                  icon={<Download className="h-3.5 w-3.5" />}
                  onClick={() => nostalgiaStorage.exportBackupJson()}
                  className="text-xs"
                >
                  Xuất file
                </Button>
              )}
            </div>

            {savedProjects.length === 0 ? (
              <Card className="p-6 text-center text-xs text-fg-subtle">
                Chưa có kịch bản tự tạo nào. Hãy sang tab <strong>"Tạo theo ký ức riêng"</strong> để bắt đầu!
              </Card>
            ) : (
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                {savedProjects.map((p) => (
                  <Card key={p.id} className="p-3.5 space-y-2 flex flex-col justify-between">
                    <div className="space-y-1.5">
                      <div className="flex items-center justify-between">
                        <span className="font-mono text-[11px] font-medium text-fg-muted bg-muted px-1.5 py-0.5 rounded">
                          {p.script.code}
                        </span>
                        <button
                          type="button"
                          onClick={() => {
                            if (confirm(`Bạn có chắc muốn xóa "${p.script.title}"?`)) {
                              nostalgiaStorage.deleteProject(p.id);
                              setSavedProjects(nostalgiaStorage.getSavedProjects());
                              toast.success('Đã xóa kịch bản');
                            }
                          }}
                          className="p-1 text-fg-subtle hover:text-fg"
                        >
                          <Trash2 className="h-3.5 w-3.5" />
                        </button>
                      </div>
                      <h4 className="font-semibold text-fg text-xs line-clamp-1">
                        {p.script.title}
                      </h4>
                      <p className="text-[11px] text-fg-subtle line-clamp-2">
                        {p.script.synopsis}
                      </p>
                    </div>
                    <div className="pt-2 border-t border-line flex items-center justify-between gap-2">
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => setSelectedScript(p.script)}
                        className="text-xs"
                      >
                        Chi tiết
                      </Button>
                      <Button
                        variant="primary"
                        size="sm"
                        icon={<Clapperboard className="h-3.5 w-3.5" />}
                        onClick={() => handleSendToStudio(p.script)}
                        className="text-xs font-medium"
                      >
                        Dựng Video
                      </Button>
                    </div>
                  </Card>
                ))}
              </div>
            )}
          </div>
        </div>
      )}

      {/* DETAIL MODAL: Mobile Full-screen / Desktop Dialog with Sticky Bottom Bar */}
      {selectedScript && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-0 sm:p-4 backdrop-blur-sm">
          <div className="h-full sm:h-auto sm:max-h-[90vh] w-full max-w-3xl flex flex-col bg-surface sm:rounded-2xl border border-line shadow-2xl overflow-hidden">
            {/* Modal Sticky Header */}
            <div className="sticky top-0 z-10 flex items-start justify-between gap-3 border-b border-line bg-surface/95 backdrop-blur px-4 py-3 sm:px-6 sm:py-4">
              <div className="space-y-1 pr-2">
                <div className="flex flex-wrap items-center gap-1.5 text-xs">
                  <span className="font-mono text-[11px] font-semibold text-fg-muted bg-muted px-1.5 py-0.5 rounded">
                    {selectedScript.code}
                  </span>
                  <span className="text-[11px] text-fg-subtle">
                    {selectedScript.region} • {selectedScript.era}
                  </span>
                  <span className="text-[11px] text-fg-muted font-medium">
                    🎭 {selectedScript.mood}
                  </span>
                </div>
                <h3 className="font-bold text-fg text-sm sm:text-base leading-snug">
                  {selectedScript.title}
                </h3>
              </div>

              <button
                type="button"
                onClick={() => setSelectedScript(null)}
                className="p-2 rounded-lg text-fg-subtle hover:text-fg hover:bg-muted transition-colors shrink-0"
                aria-label="Đóng"
              >
                <X className="h-5 w-5" />
              </button>
            </div>

            {/* Modal Scrollable Body */}
            <div className="flex-1 overflow-y-auto p-4 sm:p-6 space-y-4 text-xs">
              {/* Hook banner */}
              <div className="p-3 rounded-lg bg-subtle border border-line text-xs text-fg-muted italic leading-relaxed">
                <strong>Hook mở đầu (0-5s):</strong> "{selectedScript.hook}"
              </div>

              {/* 5 Scenes Breakdown */}
              <div className="space-y-3">
                <h4 className="font-semibold text-xs text-fg uppercase tracking-wider flex items-center gap-1.5">
                  <Film className="h-3.5 w-3.5" />
                  <span>5 Phân cảnh chuẩn 2 phút:</span>
                </h4>

                {selectedScript.scenes.map((scene) => (
                  <div
                    key={scene.sceneNumber}
                    className="rounded-lg border border-line p-3 sm:p-3.5 space-y-2.5 bg-canvas"
                  >
                    {/* Scene header */}
                    <div className="flex items-center justify-between border-b border-line/60 pb-1.5 font-medium">
                      <span className="text-fg text-xs font-semibold">
                        Cảnh {scene.sceneNumber}: {scene.title}
                      </span>
                      <span className="font-mono text-[11px] text-fg-subtle bg-muted px-1.5 py-0.5 rounded">
                        ⏱️ {scene.timecode}
                      </span>
                    </div>

                    {/* Vietnamese Visual Details */}
                    <div className="space-y-1">
                      <div className="flex items-center gap-1 text-fg font-medium">
                        <Camera className="h-3 w-3" />
                        <span>Mô tả bối cảnh & hình ảnh:</span>
                      </div>
                      <p className="text-fg-muted pl-2.5 border-l border-line leading-relaxed">
                        {scene.visualVi}
                      </p>
                    </div>

                    {/* English Master Prompt */}
                    <div className="space-y-1 bg-subtle p-2.5 rounded-lg border border-line">
                      <div className="flex items-center justify-between">
                        <span className="font-mono text-[10px] font-semibold text-fg">
                          🤖 MASTER PROMPT (AI VIDEO):
                        </span>
                        <button
                          type="button"
                          onClick={() => handleCopy(scene.aiPromptEn, `p_${scene.sceneNumber}`, `Prompt cảnh ${scene.sceneNumber}`)}
                          className="text-[11px] text-fg-subtle hover:text-fg flex items-center gap-1"
                        >
                          <Copy className="h-3 w-3" /> Copy
                        </button>
                      </div>
                      <p className="font-mono text-[11px] text-fg-muted leading-relaxed select-all">
                        {scene.aiPromptEn}
                      </p>
                    </div>

                    {/* Voiceover & Sound */}
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 pt-1 text-xs">
                      <div className="space-y-0.5">
                        <span className="text-fg-subtle font-medium flex items-center gap-1">
                          <Volume2 className="h-3 w-3" /> Lời bình (Voiceover):
                        </span>
                        <p className="text-fg italic pl-2 border-l border-line leading-relaxed">
                          "{scene.voiceover}"
                        </p>
                      </div>

                      <div className="space-y-0.5">
                        <span className="text-fg-subtle font-medium">
                          🎵 Âm thanh (SFX):
                        </span>
                        <p className="text-fg-muted pl-2 border-l border-line leading-relaxed">
                          {scene.sound}
                        </p>
                      </div>
                    </div>
                  </div>
                ))}
              </div>

              {/* Ending note */}
              <div className="p-3 rounded-lg bg-subtle border border-line text-xs text-fg-muted">
                <strong>Lời kết lắng đọng:</strong> "{selectedScript.endingNote}"
              </div>
            </div>

            {/* Modal Sticky Bottom Action Bar */}
            <div className="sticky bottom-0 z-10 flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-2 border-t border-line bg-surface/95 backdrop-blur px-4 py-3 sm:px-6">
              <div className="flex items-center gap-1.5 overflow-x-auto no-scrollbar pb-1 sm:pb-0">
                <Button
                  variant="secondary"
                  size="sm"
                  icon={copiedKey === 'modal_voice' ? <Check className="h-3.5 w-3.5" /> : <Volume2 className="h-3.5 w-3.5" />}
                  onClick={() => handleCopy(extractNostalgiaVoiceover(selectedScript), 'modal_voice', 'Lời bình')}
                  className="text-xs px-2.5 whitespace-nowrap"
                >
                  Copy Lời bình
                </Button>

                <Button
                  variant="secondary"
                  size="sm"
                  icon={copiedKey === 'modal_prompts' ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
                  onClick={() => handleCopy(extractNostalgiaAIPrompts(selectedScript), 'modal_prompts', 'Prompt AI')}
                  className="text-xs px-2.5 whitespace-nowrap"
                >
                  Copy Prompt AI
                </Button>

                <Button
                  variant="secondary"
                  size="sm"
                  icon={<FileText className="h-3.5 w-3.5" />}
                  onClick={() => handleCopy(formatNostalgiaProductionScript(selectedScript), 'modal_full', 'Toàn bộ kịch bản')}
                  className="text-xs px-2.5 whitespace-nowrap hidden sm:inline-flex"
                >
                  Copy kịch bản
                </Button>
              </div>

              <Button
                variant="primary"
                size="sm"
                icon={<Clapperboard className="h-4 w-4" />}
                onClick={() => {
                  setSelectedScript(null);
                  handleSendToStudio(selectedScript);
                }}
                className="text-xs sm:text-sm font-medium w-full sm:w-auto"
              >
                Dựng Video (Studio)
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
