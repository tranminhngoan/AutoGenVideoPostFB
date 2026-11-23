import React, { useState, useEffect, useMemo } from 'react';
import {
  Search,
  Sparkles,
  Copy,
  Check,
  Download,
  Upload,
  Trash2,
  Edit3,
  ArrowRight,
  ExternalLink,
  Clapperboard,
  Clock,
  Volume2,
  Video,
  Eye,
  RefreshCw,
  Wand2,
  Plus,
  Heart,
  FileText,
  ChevronRight,
  SlidersHorizontal,
  Bookmark,
  Share2,
  Film,
  Camera,
  MapPin,
  Calendar,
  Layers,
} from 'lucide-react';
import {
  Badge,
  Button,
  Card,
  Field,
  Input,
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
  NostalgiaScene,
  NostalgiaCategoryKey,
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
      // Category filter
      if (selectedCategory !== 'all' && script.category !== selectedCategory) {
        return false;
      }
      // Region filter
      if (selectedRegion !== 'all' && script.region !== selectedRegion) {
        return false;
      }
      // Mood filter
      if (selectedMood !== 'all' && script.mood !== selectedMood) {
        return false;
      }
      // Era filter
      if (selectedEra !== 'all' && script.era !== selectedEra) {
        return false;
      }
      // Search query (title, code, hook, synopsis, visual details)
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
    toast.success(isFav ? 'Đã thêm vào kịch bản yêu thích' : 'Đã bỏ yêu thích');
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

      toast.success(`Đã chuẩn bị 5 phân cảnh chuẩn AI cho "${script.title}". Đang mở Studio...`);
      navigate('/studio/new');
    } catch (e: any) {
      toast.error('Lỗi khi nạp kịch bản vào Studio: ' + (e?.message || 'Không xác định'));
    }
  };

  // Generate Custom Script
  const handleCreateCustomScript = () => {
    if (!customMemoryText.trim()) {
      toast.info('Vui lòng nhập một kỷ niệm tuổi thơ của bạn (ví dụ: bữa cơm chiều mưa, chiếc xe đạp đứt xích của cha...)');
      return;
    }
    const script = generateCustomChildhoodScript({
      userMemory: customMemoryText,
      region: customRegion,
      era: customEra,
      mood: customMood,
    });
    setGeneratedCustomScript(script);
    toast.success('Đã sinh thành công kịch bản 5 phân cảnh chi tiết chuẩn 2 phút!');
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
    toast.success(`Đã lưu "${script.title}" vào danh sách kịch bản cá nhân!`);
  };

  return (
    <div className="space-y-6 pb-20">
      {/* Page Header */}
      <PageHeader
        title="Lên Kịch Bản - Ký Ức Tuổi Thơ 8x 9x"
        description="Kho 300+ kịch bản hoài niệm tuổi thơ cơ hàn mà bình yên. Tái hiện từng mái nhà tranh, đường đất bùn lầy, bữa cơm độn sắn khoai, áo may ô cháo lòng, đôi dép tổ ong mòn rách. Tối ưu prompt chi tiết từng góc máy, ánh sáng và cử chỉ cho AI Video Generator (Kling AI, Midjourney, Hailuo, Runway Gen-3)."
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="secondary"
              size="sm"
              icon={<Download className="h-4 w-4" />}
              onClick={() => nostalgiaStorage.exportBackupJson()}
              title="Sao lưu toàn bộ kịch bản ra file JSON"
            >
              Sao lưu JSON
            </Button>
            <Button
              variant="primary"
              size="sm"
              icon={<Clapperboard className="h-4 w-4" />}
              onClick={() => navigate('/studio/new')}
            >
              Mở Studio Video AI
            </Button>
          </div>
        }
      />

      {/* Emotional Nostalgic Banner */}
      <div className="relative overflow-hidden rounded-xl border border-amber-500/20 bg-gradient-to-r from-amber-950/40 via-surface to-amber-900/20 p-4 sm:p-5 shadow-sm">
        <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
          <div className="space-y-1">
            <div className="flex items-center gap-2">
              <span className="text-xl">🌾</span>
              <span className="font-semibold text-amber-200 text-sm sm:text-base">
                "Bao nhiêu của cải phố thị cũng chẳng thể mua lại được một chiều mưa bên mẹ..."
              </span>
            </div>
            <p className="text-xs text-fg-subtle max-w-3xl leading-relaxed">
              Mỗi kịch bản gồm đúng <strong>5 phân cảnh chuẩn 2 phút</strong>, mô tả chân thực từ vết nứt tường vách đất, vệt muối mồ hôi trên lưng áo cha, đến Master Prompt tiếng Anh chuyên biệt cho các mô hình AI tạo video thế hệ mới.
            </p>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            <Badge tone="success" className="font-mono text-xs">
              300 Kịch bản chuẩn 8x 9x
            </Badge>
            <Badge tone="info" className="text-xs">
              0% Database (Tiết kiệm RAM)
            </Badge>
          </div>
        </div>
      </div>

      {/* Main Tabs Navigation */}
      <div className="flex border-b border-line gap-2 overflow-x-auto pb-1 text-sm font-medium">
        <button
          type="button"
          onClick={() => setActiveTab('library')}
          className={cn(
            'flex items-center gap-2 px-4 py-2.5 border-b-2 transition-colors whitespace-nowrap',
            activeTab === 'library'
              ? 'border-brand text-brand font-semibold'
              : 'border-transparent text-fg-muted hover:text-fg hover:border-line'
          )}
        >
          <Layers className="h-4 w-4" />
          <span>Thư Viện 300 Kịch Bản (6 Chủ Đề)</span>
          <span className="rounded-full bg-brand/10 px-2 py-0.5 text-xs text-brand">300</span>
        </button>

        <button
          type="button"
          onClick={() => setActiveTab('custom')}
          className={cn(
            'flex items-center gap-2 px-4 py-2.5 border-b-2 transition-colors whitespace-nowrap',
            activeTab === 'custom'
              ? 'border-brand text-brand font-semibold'
              : 'border-transparent text-fg-muted hover:text-fg hover:border-line'
          )}
        >
          <Wand2 className="h-4 w-4" />
          <span>Tạo Kịch Bản Ký Ức Riêng</span>
          <span className="rounded-full bg-amber-500/10 px-2 py-0.5 text-xs text-amber-400">AI Viết</span>
        </button>

        <button
          type="button"
          onClick={() => setActiveTab('saved')}
          className={cn(
            'flex items-center gap-2 px-4 py-2.5 border-b-2 transition-colors whitespace-nowrap',
            activeTab === 'saved'
              ? 'border-brand text-brand font-semibold'
              : 'border-transparent text-fg-muted hover:text-fg hover:border-line'
          )}
        >
          <Bookmark className="h-4 w-4" />
          <span>Kịch Bản Đã Lưu & Yêu Thích</span>
          <span className="rounded-full bg-muted px-2 py-0.5 text-xs text-fg-muted">
            {savedProjects.length + favorites.length}
          </span>
        </button>
      </div>

      {/* TAB 1: THƯ VIỆN KỊCH BẢN (6 CHỦ ĐỀ) */}
      {activeTab === 'library' && (
        <div className="space-y-6">
          {/* Category Chips Bar */}
          <div className="space-y-2">
            <div className="text-xs font-semibold text-fg-muted uppercase tracking-wider">
              Chọn chủ đề ký ức thời 8x 9x (Mỗi chủ đề 50 kịch bản):
            </div>
            <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-2.5">
              <button
                type="button"
                onClick={() => setSelectedCategory('all')}
                className={cn(
                  'flex flex-col items-start p-3 rounded-lg border text-left transition-all',
                  selectedCategory === 'all'
                    ? 'border-brand bg-brand/10 shadow-sm'
                    : 'border-line bg-surface hover:bg-muted/50'
                )}
              >
                <div className="flex items-center justify-between w-full mb-1">
                  <span className="text-lg">✨</span>
                  <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-muted text-fg-muted">Tất cả</span>
                </div>
                <div className="font-semibold text-xs text-fg">Toàn Bộ Ký Ức</div>
                <div className="text-[11px] text-fg-subtle line-clamp-1">300 kịch bản tổng hợp</div>
              </button>

              {NOSTALGIA_CATEGORIES.map((cat) => (
                <button
                  key={cat.id}
                  type="button"
                  onClick={() => setSelectedCategory(cat.id)}
                  className={cn(
                    'flex flex-col items-start p-3 rounded-lg border text-left transition-all',
                    selectedCategory === cat.id
                      ? 'border-brand bg-brand/10 shadow-sm ring-1 ring-brand/30'
                      : 'border-line bg-surface hover:bg-muted/50'
                  )}
                >
                  <div className="flex items-center justify-between w-full mb-1">
                    <span className="text-lg">{cat.icon}</span>
                    <span className="text-[10px] font-mono font-medium px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-300">
                      50 KB
                    </span>
                  </div>
                  <div className="font-semibold text-xs text-fg line-clamp-1">{cat.name}</div>
                  <div className="text-[11px] text-fg-subtle line-clamp-1">{cat.description}</div>
                </button>
              ))}
            </div>
          </div>

          {/* Search & Advanced Filters */}
          <div className="flex flex-col lg:flex-row gap-3 items-stretch lg:items-center justify-between rounded-lg border border-line bg-surface p-3.5">
            <div className="relative flex-1">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-fg-subtle" />
              <input
                type="text"
                placeholder="Tìm kịch bản theo từ khóa: cơm độn sắn, que kem mút, cánh diều, mẹ vá áo, dép tổ ong, tắm sông..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="w-full pl-9 pr-4 py-2 text-sm rounded-md border border-line bg-canvas focus:border-brand focus:outline-none"
              />
              {searchQuery && (
                <button
                  type="button"
                  onClick={() => setSearchQuery('')}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-xs text-fg-subtle hover:text-fg"
                >
                  ✕
                </button>
              )}
            </div>

            <div className="flex flex-wrap items-center gap-2 text-xs">
              {/* Region filter */}
              <select
                value={selectedRegion}
                onChange={(e) => setSelectedRegion(e.target.value)}
                className="rounded-md border border-line bg-canvas px-2.5 py-1.5 text-xs text-fg focus:outline-none"
              >
                <option value="all">🌍 Mọi miền quê</option>
                <option value="Bắc Bộ">🌾 Làng quê Bắc Bộ</option>
                <option value="Miền Trung">🌊 Miền Trung nắng gió</option>
                <option value="Miền Tây">🚣 Miền Tây sông nước</option>
              </select>

              {/* Mood filter */}
              <select
                value={selectedMood}
                onChange={(e) => setSelectedMood(e.target.value)}
                className="rounded-md border border-line bg-canvas px-2.5 py-1.5 text-xs text-fg focus:outline-none"
              >
                <option value="all">🎭 Tất cả cảm xúc</option>
                <option value="Nghẹn ngào rớt nước mắt">😢 Nghẹn ngào rớt nước mắt</option>
                <option value="Da diết nhớ cha mẹ">👵 Da diết nhớ cha mẹ</option>
                <option value="Ấm áp hoài niệm">🍂 Ấm áp hoài niệm</option>
                <option value="Xót xa thương quê">💔 Xót xa thương quê</option>
                <option value="Hóm hỉnh ngây ngô">😄 Hóm hỉnh ngây ngô</option>
              </select>

              {/* Era filter */}
              <select
                value={selectedEra}
                onChange={(e) => setSelectedEra(e.target.value)}
                className="rounded-md border border-line bg-canvas px-2.5 py-1.5 text-xs text-fg focus:outline-none"
              >
                <option value="all">⏳ Thời kỳ 8x & 9x</option>
                <option value="8x">Thập niên 80s (Thời bao cấp)</option>
                <option value="9x">Thập niên 90s (Đổi mới nghèo)</option>
              </select>

              {(searchQuery || selectedCategory !== 'all' || selectedRegion !== 'all' || selectedMood !== 'all' || selectedEra !== 'all') && (
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    setSearchQuery('');
                    setSelectedCategory('all');
                    setSelectedRegion('all');
                    setSelectedMood('all');
                    setSelectedEra('all');
                  }}
                  className="text-xs text-brand"
                >
                  Đặt lại lọc
                </Button>
              )}
            </div>
          </div>

          {/* Results Counter */}
          <div className="flex items-center justify-between text-xs text-fg-subtle px-1">
            <span>
              Hiển thị <strong>{filteredScripts.length}</strong> / 300 kịch bản hoài niệm
            </span>
            <span className="italic">
              * Nhấn "Dựng Video (Studio)" để tự động tạo 5 phân cảnh vào Studio AI
            </span>
          </div>

          {/* Scripts Grid */}
          {filteredScripts.length === 0 ? (
            <Card className="p-12 text-center space-y-3">
              <div className="text-4xl">🌾</div>
              <h4 className="font-semibold text-fg">Không tìm thấy kịch bản phù hợp</h4>
              <p className="text-xs text-fg-subtle max-w-md mx-auto">
                Không tìm thấy kịch bản nào khớp với bộ lọc. Hãy thử tìm từ khóa khác (ví dụ: "cơm độn", "diều", "bếp", "dép") hoặc xóa bớt tiêu chí lọc.
              </p>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => {
                  setSearchQuery('');
                  setSelectedCategory('all');
                  setSelectedRegion('all');
                  setSelectedMood('all');
                }}
              >
                Xem toàn bộ 300 kịch bản
              </Button>
            </Card>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {filteredScripts.map((script) => {
                const isFav = favorites.includes(script.id);
                return (
                  <Card
                    key={script.id}
                    className="flex flex-col justify-between p-4 space-y-3 hover:border-brand/50 transition-all group"
                  >
                    <div className="space-y-2">
                      {/* Top Badges */}
                      <div className="flex items-start justify-between gap-2">
                        <div className="flex flex-wrap items-center gap-1.5">
                          <span className="font-mono text-[11px] font-semibold text-amber-400 bg-amber-500/10 px-1.5 py-0.5 rounded">
                            {script.code}
                          </span>
                          <Badge tone="neutral" className="text-[10px]">
                            {script.region}
                          </Badge>
                          <Badge tone="info" className="text-[10px]">
                            {script.era}
                          </Badge>
                        </div>
                        <button
                          type="button"
                          onClick={() => handleToggleFavorite(script.id)}
                          className={cn(
                            'p-1.5 rounded-full hover:bg-muted transition-colors',
                            isFav ? 'text-rose-500' : 'text-fg-subtle hover:text-fg'
                          )}
                          title={isFav ? 'Bỏ yêu thích' : 'Lưu vào yêu thích'}
                        >
                          <Heart className={cn('h-4 w-4', isFav && 'fill-current')} />
                        </button>
                      </div>

                      {/* Title */}
                      <h4 className="font-semibold text-fg text-sm group-hover:text-brand transition-colors line-clamp-2 leading-snug">
                        {script.title}
                      </h4>

                      {/* Hook & Synopsis */}
                      <p className="text-xs text-amber-200/80 italic line-clamp-2 bg-amber-500/5 p-2 rounded border border-amber-500/10">
                        "{script.hook}"
                      </p>

                      <p className="text-xs text-fg-subtle line-clamp-2 leading-relaxed">
                        {script.synopsis}
                      </p>

                      {/* Mood Badge */}
                      <div className="flex items-center gap-2 pt-1 text-[11px] text-fg-subtle">
                        <span className="text-rose-400 font-medium">🎭 {script.mood}</span>
                        <span>•</span>
                        <span>⏱️ ~2 phút (5 scenes)</span>
                      </div>
                    </div>

                    {/* Action Buttons */}
                    <div className="pt-2 border-t border-line/60 flex items-center justify-between gap-2">
                      <Button
                        variant="ghost"
                        size="sm"
                        icon={<Eye className="h-3.5 w-3.5" />}
                        onClick={() => setSelectedScript(script)}
                        className="text-xs text-fg-muted hover:text-fg"
                      >
                        Chi tiết
                      </Button>

                      <div className="flex items-center gap-1.5">
                        <Button
                          variant="secondary"
                          size="sm"
                          icon={copiedKey === `prompt_${script.id}` ? <Check className="h-3.5 w-3.5 text-emerald-400" /> : <Copy className="h-3.5 w-3.5" />}
                          onClick={() => handleCopy(extractNostalgiaAIPrompts(script), `prompt_${script.id}`, 'Prompt AI Video')}
                          title="Copy Master Prompt AI Video (Kling/Midjourney)"
                          className="text-xs"
                        >
                          Prompt AI
                        </Button>

                        <Button
                          variant="primary"
                          size="sm"
                          icon={<Clapperboard className="h-3.5 w-3.5" />}
                          onClick={() => handleSendToStudio(script)}
                          title="Nạp kịch bản và 5 shots vào Studio Video AI"
                          className="text-xs font-medium bg-emerald-600 hover:bg-emerald-500 text-white"
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
        <div className="space-y-6 max-w-4xl mx-auto">
          <Card className="p-5 sm:p-6 space-y-5 border-amber-500/30">
            <div className="space-y-1">
              <div className="flex items-center gap-2">
                <span className="text-xl">✍️</span>
                <h3 className="font-semibold text-fg text-base sm:text-lg">
                  Lên Kịch Bản Tuổi Thơ Từ Ký Ức Của Chính Bạn
                </h3>
              </div>
              <p className="text-xs text-fg-subtle leading-relaxed">
                Hãy kể lại một mẩu ký ức thời 8x 9x của bạn (bữa cơm gia đình, một lần đi chăn trâu bị ướt mưa, mẹ mua chiếc xe đạp cũ...). Hệ thống sẽ tự động bóc tách thành <strong>kịch bản chuẩn 2 phút gồm 5 phân cảnh</strong> với đầy đủ mô tả hình ảnh cực kỳ chi tiết (ngôi nhà tranh, tường vách đất, áo vá, dép tổ ong mòn đế) và Master Prompt tiếng Anh cho AI Video Generator.
              </p>
            </div>

            <div className="space-y-4">
              <Field label="Kể lại mẩu ký ức tuổi thơ của bạn (Càng cụ thể càng chân thực):">
                <Textarea
                  rows={4}
                  value={customMemoryText}
                  onChange={(e) => setCustomMemoryText(e.target.value)}
                  placeholder="Ví dụ: Nhớ chiều mưa năm 1996 ở quê nghèo Bắc Bộ, bố đạp chiếc xe đạp Thống Nhất rỉ sét đèo tôi đi thi học sinh giỏi huyện. Đường đất trơn trượt bị đứt xích, hai bố con bì bõm dắt bộ lội bùn, bố cởi chiếc áo mưa nilon rách che kín người tôi còn mình thì ướt sũng..."
                />
              </Field>

              <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                <Field label="Vùng miền quê hương:">
                  <Select
                    value={customRegion}
                    onChange={(e) => setCustomRegion(e.target.value as any)}
                  >
                    <option value="Bắc Bộ">Làng quê Bắc Bộ (Nhà ngói, giếng nước, rặng tre)</option>
                    <option value="Miền Trung">Miền Trung (Gió Lào, cát trắng, phi lao)</option>
                    <option value="Miền Tây">Miền Tây sông nước (Nhà sàn, cầu khỉ, ghe xuồng)</option>
                    <option value="Chung">Làng quê Việt Nam chung</option>
                  </Select>
                </Field>

                <Field label="Thời kỳ tuổi thơ:">
                  <Select
                    value={customEra}
                    onChange={(e) => setCustomEra(e.target.value as any)}
                  >
                    <option value="8x">Thế hệ 8x (Thập niên 80 - đầu 90)</option>
                    <option value="9x">Thế hệ 9x (Thập niên 90 - 2000)</option>
                  </Select>
                </Field>

                <Field label="Tông cảm xúc kịch bản:">
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

              <div className="flex justify-end pt-2">
                <Button
                  variant="primary"
                  icon={<Wand2 className="h-4 w-4" />}
                  onClick={handleCreateCustomScript}
                  className="bg-amber-600 hover:bg-amber-500 text-white font-medium"
                >
                  Sinh Kịch Bản Chuẩn 2 Phút Ngay
                </Button>
              </div>
            </div>
          </Card>

          {/* Generated Custom Script Result Preview */}
          {generatedCustomScript && (
            <Card className="p-5 space-y-4 border-emerald-500/40 bg-surface">
              <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 border-b border-line pb-3">
                <div>
                  <div className="flex items-center gap-2">
                    <span className="font-mono text-xs font-bold text-amber-400 bg-amber-500/10 px-2 py-0.5 rounded">
                      {generatedCustomScript.code}
                    </span>
                    <Badge tone="success" className="text-xs">
                      Đã sinh 5 Phân cảnh (~2 Phút)
                    </Badge>
                  </div>
                  <h3 className="font-bold text-fg text-base mt-1">
                    {generatedCustomScript.title}
                  </h3>
                </div>

                <div className="flex items-center gap-2">
                  <Button
                    variant="secondary"
                    size="sm"
                    icon={<Bookmark className="h-4 w-4" />}
                    onClick={() => handleSaveCustomProject(generatedCustomScript)}
                  >
                    Lưu kịch bản
                  </Button>
                  <Button
                    variant="primary"
                    size="sm"
                    icon={<Clapperboard className="h-4 w-4" />}
                    onClick={() => handleSendToStudio(generatedCustomScript)}
                    className="bg-emerald-600 hover:bg-emerald-500 text-white"
                  >
                    Dựng Video (Studio)
                  </Button>
                </div>
              </div>

              <div className="space-y-3">
                <div className="bg-amber-500/10 p-3 rounded-md border border-amber-500/20 text-xs text-amber-200">
                  <strong>Câu Hook mở đầu (0-5s):</strong> "{generatedCustomScript.hook}"
                </div>

                {/* Scenes Preview */}
                <div className="space-y-3 pt-2">
                  <h5 className="font-semibold text-xs text-fg uppercase tracking-wider">
                    Chi tiết 5 phân cảnh điện ảnh:
                  </h5>
                  {generatedCustomScript.scenes.map((scene) => (
                    <div
                      key={scene.sceneNumber}
                      className="rounded-lg border border-line p-3 space-y-2 bg-canvas/60 text-xs"
                    >
                      <div className="flex items-center justify-between font-medium text-fg">
                        <span className="text-amber-400">
                          [Cảnh {scene.sceneNumber}] ({scene.timecode}) - {scene.title}
                        </span>
                        <span className="text-fg-subtle">{scene.durationSeconds}s</span>
                      </div>

                      <div className="space-y-1">
                        <span className="text-fg-subtle font-semibold">🎥 Hình ảnh chi tiết:</span>
                        <p className="text-fg-muted pl-2 border-l-2 border-amber-500/30">
                          {scene.visualVi}
                        </p>
                      </div>

                      <div className="space-y-1 bg-muted/40 p-2 rounded">
                        <span className="text-emerald-400 font-semibold font-mono text-[11px]">
                          🤖 Master Prompt AI Video:
                        </span>
                        <p className="text-fg font-mono text-[11px] leading-relaxed select-all">
                          {scene.aiPromptEn}
                        </p>
                      </div>

                      <div className="space-y-1">
                        <span className="text-fg-subtle font-semibold">🎙️ Lời bình / Voice:</span>
                        <p className="text-fg italic pl-2 border-l-2 border-brand/30">
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

      {/* TAB 3: KỊCH BẢN ĐÃ LƯU & YÊU THÍCH */}
      {activeTab === 'saved' && (
        <div className="space-y-6">
          {/* Favorites List */}
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <h3 className="font-semibold text-fg text-sm flex items-center gap-2">
                <Heart className="h-4 w-4 text-rose-500 fill-current" />
                <span>Kịch Bản Mẫu Yêu Thích ({favorites.length})</span>
              </h3>
            </div>

            {favorites.length === 0 ? (
              <Card className="p-6 text-center text-xs text-fg-subtle">
                Bạn chưa lưu kịch bản mẫu nào vào danh sách yêu thích. Hãy bấm vào biểu tượng trái tim ở các kịch bản trong thư viện để lưu nhanh tại đây.
              </Card>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                {favorites.map((favId) => {
                  const script = getNostalgiaScriptById(favId);
                  if (!script) return null;
                  return (
                    <Card key={script.id} className="p-3.5 space-y-2 flex flex-col justify-between">
                      <div className="space-y-1.5">
                        <div className="flex items-center justify-between">
                          <span className="font-mono text-[11px] font-semibold text-amber-400">
                            {script.code}
                          </span>
                          <button
                            type="button"
                            onClick={() => handleToggleFavorite(script.id)}
                            className="text-rose-500 hover:text-fg-subtle p-1"
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
                          Xem chi tiết
                        </Button>
                        <Button
                          variant="primary"
                          size="sm"
                          icon={<Clapperboard className="h-3.5 w-3.5" />}
                          onClick={() => handleSendToStudio(script)}
                          className="text-xs bg-emerald-600 hover:bg-emerald-500 text-white"
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
                <Bookmark className="h-4 w-4 text-brand" />
                <span>Kịch Bản Tự Tạo Đã Lưu ({savedProjects.length})</span>
              </h3>
              {savedProjects.length > 0 && (
                <Button
                  variant="secondary"
                  size="sm"
                  icon={<Download className="h-3.5 w-3.5" />}
                  onClick={() => nostalgiaStorage.exportBackupJson()}
                  className="text-xs"
                >
                  Xuất file Backup
                </Button>
              )}
            </div>

            {savedProjects.length === 0 ? (
              <Card className="p-6 text-center text-xs text-fg-subtle">
                Chưa có kịch bản tùy chỉnh nào được lưu. Hãy sang tab <strong>"Tạo Kịch Bản Ký Ức Riêng"</strong> để sáng tác câu chuyện của bạn!
              </Card>
            ) : (
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
                {savedProjects.map((p) => (
                  <Card key={p.id} className="p-3.5 space-y-2 flex flex-col justify-between">
                    <div className="space-y-1.5">
                      <div className="flex items-center justify-between">
                        <span className="font-mono text-[11px] font-semibold text-amber-400">
                          {p.script.code}
                        </span>
                        <button
                          type="button"
                          onClick={() => {
                            if (confirm(`Bạn có chắc muốn xóa kịch bản "${p.script.title}"?`)) {
                              nostalgiaStorage.deleteProject(p.id);
                              setSavedProjects(nostalgiaStorage.getSavedProjects());
                              toast.success('Đã xóa kịch bản');
                            }
                          }}
                          className="text-fg-subtle hover:text-rose-400 p-1"
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
                        Xem chi tiết
                      </Button>
                      <Button
                        variant="primary"
                        size="sm"
                        icon={<Clapperboard className="h-3.5 w-3.5" />}
                        onClick={() => handleSendToStudio(p.script)}
                        className="text-xs bg-emerald-600 hover:bg-emerald-500 text-white"
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

      {/* DETAIL MODAL: Xem Toàn Bộ 5 Phân Cảnh & Copy Kịch Bản */}
      {selectedScript && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/75 p-3 sm:p-5 backdrop-blur-sm overflow-y-auto">
          <Card className="max-h-[92vh] w-full max-w-4xl overflow-y-auto p-4 sm:p-6 space-y-5 shadow-2xl border-amber-500/30 bg-surface">
            {/* Modal Header */}
            <div className="flex items-start justify-between gap-3 border-b border-line pb-4">
              <div className="space-y-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-mono text-xs font-bold text-amber-400 bg-amber-500/10 px-2 py-0.5 rounded">
                    {selectedScript.code}
                  </span>
                  <Badge tone="neutral" className="text-xs">
                    {selectedScript.region}
                  </Badge>
                  <Badge tone="info" className="text-xs">
                    {selectedScript.era}
                  </Badge>
                  <span className="text-xs text-rose-400 font-medium">
                    🎭 {selectedScript.mood}
                  </span>
                </div>
                <h3 className="font-bold text-fg text-lg sm:text-xl">
                  {selectedScript.title}
                </h3>
                <p className="text-xs text-amber-200/90 italic">
                  Hook mở màn: "{selectedScript.hook}"
                </p>
              </div>

              <Button
                variant="ghost"
                size="sm"
                onClick={() => setSelectedScript(null)}
                className="text-fg-subtle hover:text-fg text-sm"
              >
                ✕ Đóng
              </Button>
            </div>

            {/* Quick Action Bar inside Modal */}
            <div className="flex flex-wrap items-center justify-between gap-2 p-3 rounded-lg bg-canvas border border-line">
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  variant="secondary"
                  size="sm"
                  icon={copiedKey === 'modal_voice' ? <Check className="h-3.5 w-3.5 text-emerald-400" /> : <Volume2 className="h-3.5 w-3.5" />}
                  onClick={() => handleCopy(extractNostalgiaVoiceover(selectedScript), 'modal_voice', 'Lời thuyết minh')}
                  className="text-xs"
                >
                  Copy Lời Bình (CapCut/TTS)
                </Button>

                <Button
                  variant="secondary"
                  size="sm"
                  icon={copiedKey === 'modal_prompts' ? <Check className="h-3.5 w-3.5 text-emerald-400" /> : <Copy className="h-3.5 w-3.5" />}
                  onClick={() => handleCopy(extractNostalgiaAIPrompts(selectedScript), 'modal_prompts', 'Master Prompt AI')}
                  className="text-xs"
                >
                  Copy Prompt AI (Kling/Midjourney)
                </Button>

                <Button
                  variant="secondary"
                  size="sm"
                  icon={<FileText className="h-3.5 w-3.5" />}
                  onClick={() => handleCopy(formatNostalgiaProductionScript(selectedScript), 'modal_full', 'Toàn bộ kịch bản')}
                  className="text-xs"
                >
                  Copy Kịch Bản Chi Tiết
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
                className="bg-emerald-600 hover:bg-emerald-500 text-white font-medium text-xs sm:text-sm"
              >
                🚀 Dựng Video Tập Này (Studio)
              </Button>
            </div>

            {/* 5 Scenes Detailed Breakdown */}
            <div className="space-y-4">
              <h4 className="font-semibold text-xs text-fg uppercase tracking-wider flex items-center gap-2">
                <Film className="h-4 w-4 text-amber-400" />
                <span>Chi tiết 5 phân cảnh chuẩn 2 phút (Mô tả bối cảnh & Master Prompt):</span>
              </h4>

              {selectedScript.scenes.map((scene) => (
                <div
                  key={scene.sceneNumber}
                  className="rounded-lg border border-line p-4 space-y-3 bg-canvas/60 text-xs"
                >
                  {/* Scene Header */}
                  <div className="flex items-center justify-between border-b border-line/60 pb-2">
                    <span className="font-semibold text-amber-400 text-sm">
                      Phân cảnh {scene.sceneNumber}: {scene.title}
                    </span>
                    <span className="font-mono text-xs text-fg-subtle bg-muted px-2 py-0.5 rounded">
                      ⏱️ {scene.timecode} ({scene.durationSeconds}s)
                    </span>
                  </div>

                  {/* Visual Description in Vietnamese */}
                  <div className="space-y-1">
                    <div className="flex items-center gap-1.5 text-amber-200 font-medium">
                      <Camera className="h-3.5 w-3.5" />
                      <span>Mô tả hình ảnh bối cảnh làng quê 8x 9x (Chi tiết nhà đất, áo quần, dép, bùn lầy):</span>
                    </div>
                    <p className="text-fg-muted pl-3 border-l-2 border-amber-500/40 leading-relaxed text-xs">
                      {scene.visualVi}
                    </p>
                  </div>

                  {/* AI Video Prompt in English */}
                  <div className="space-y-1.5 bg-black/40 p-3 rounded-md border border-line/70">
                    <div className="flex items-center justify-between">
                      <span className="font-mono text-[11px] font-semibold text-emerald-400">
                        🤖 MASTER PROMPT CHO AI VIDEO GENERATOR (KLING AI / MIDJOURNEY / HAILUO / RUNWAY):
                      </span>
                      <button
                        type="button"
                        onClick={() => handleCopy(scene.aiPromptEn, `prompt_scene_${scene.sceneNumber}`, `Prompt cảnh ${scene.sceneNumber}`)}
                        className="text-[11px] text-fg-subtle hover:text-emerald-400 flex items-center gap-1"
                      >
                        <Copy className="h-3 w-3" /> Copy
                      </button>
                    </div>
                    <p className="font-mono text-[11px] text-fg leading-relaxed select-all">
                      {scene.aiPromptEn}
                    </p>
                  </div>

                  {/* Voiceover & Sound */}
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 pt-1">
                    <div className="space-y-1">
                      <span className="text-fg-subtle font-semibold flex items-center gap-1">
                        <Volume2 className="h-3 w-3 text-brand" /> Lời bình (Voiceover nghẹn ngào):
                      </span>
                      <p className="text-fg italic pl-2 border-l-2 border-brand/40">
                        "{scene.voiceover}"
                      </p>
                    </div>

                    <div className="space-y-1">
                      <span className="text-fg-subtle font-semibold">
                        🎵 Âm thanh môi trường (SFX):
                      </span>
                      <p className="text-fg-muted pl-2 border-l-2 border-line">
                        {scene.sound}
                      </p>
                    </div>
                  </div>
                </div>
              ))}
            </div>

            {/* Ending Note */}
            <div className="p-3 rounded-lg bg-amber-500/10 border border-amber-500/20 text-xs text-amber-200">
              <strong>Lời kết đọng lại nghẹn ngào:</strong> "{selectedScript.endingNote}"
            </div>

            {/* Modal Footer */}
            <div className="flex justify-end pt-2 border-t border-line">
              <Button
                variant="primary"
                onClick={() => {
                  setSelectedScript(null);
                  handleSendToStudio(selectedScript);
                }}
                className="bg-emerald-600 hover:bg-emerald-500 text-white font-medium"
              >
                🚀 Chuyển Thẳng Sang Studio Dựng Video AI
              </Button>
            </div>
          </Card>
        </div>
      )}
    </div>
  );
}
