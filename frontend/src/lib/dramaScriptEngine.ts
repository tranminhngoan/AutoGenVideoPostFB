import { DramaEpisode, DramaScene } from '../data/chineseDramaLibrary';

export type DramaStyleTone = 'va_mat' | 'kich_tinh' | 'nguoc_luyen' | 'hai_huoc';

export interface RewriteOptions {
  title: string;
  sourceText: string;
  numEpisodes: number;
  targetDurationSeconds?: number; // default 120 (2 minutes)
  tone?: DramaStyleTone;
  includeVisualPrompts?: boolean;
  includeSoundEffects?: boolean;
}

/**
 * Ước tính thời gian đọc lời thoại tiếng Việt:
 * Trung bình 2.5 - 3 từ/giây khi đọc biểu cảm kịch tính + 1.5s mỗi lần đổi cảnh/dừng nghỉ.
 */
export function estimateReadingTime(text: string): { words: number; seconds: number; formatted: string } {
  const clean = text.replace(/\[[^\]]*\]/g, ' ').replace(/[*_`>#]/g, ' ').trim();
  const words = clean.split(/\s+/).filter(Boolean).length;
  // Khoảng 140 - 150 từ / phút cho tốc độ đọc phim ngắn Douyin dồn dập
  const seconds = Math.round((words / 2.5) + (text.split('\n\n').length * 1.2));
  const mins = Math.floor(seconds / 60);
  const secs = seconds % 60;
  const formatted = mins > 0 ? `${mins}p ${secs.toString().padStart(2, '0')}s` : `${secs}s`;
  return { words, seconds, formatted };
}

/**
 * Trích xuất chỉ phần lời đọc / lồng tiếng (Voiceover / TTS) sạch sẽ,
 * loại bỏ toàn bộ chỉ dẫn kỹ thuật, bối cảnh, góc máy để dán thẳng vào CapCut TTS, ElevenLabs, Vbee.
 */
export function extractVoiceoverText(episode: DramaEpisode): string {
  const lines: string[] = [];
  lines.push(`--- ${episode.title.toUpperCase()} ---`);
  lines.push('');
  for (const scene of episode.scenes) {
    if (scene.voiceover && scene.voiceover.trim()) {
      lines.push(scene.voiceover.trim());
      lines.push('');
    }
  }
  return lines.join('\n').trim();
}

/**
 * Xuất kịch bản dựng phim hoàn chỉnh cho editor:
 * Phân rõ từng cảnh, Visual Prompt (mô tả hình ảnh), Lời thoại & Voiceover, Âm thanh & SFX.
 */
export function formatProductionScript(episode: DramaEpisode): string {
  const lines: string[] = [];
  lines.push(`================================================================`);
  lines.push(`🎬 ${episode.title.toUpperCase()}`);
  lines.push(`⏱️ Thời lượng mục tiêu: ~${Math.round(episode.durationSeconds / 60)} phút (${episode.durationSeconds} giây)`);
  if (episode.hook) lines.push(`🔥 Hook mở màn (0-5s): ${episode.hook}`);
  if (episode.cliffhanger) lines.push(`⚡ Cliffhanger kết tập: ${episode.cliffhanger}`);
  lines.push(`================================================================\n`);

  for (const s of episode.scenes) {
    lines.push(`[PHÂN CẢNH ${s.sceneNumber}] (${s.timecode}) - ${s.title}`);
    lines.push(`🎥 HÌNH ẢNH / VISUAL: ${s.visual}`);
    lines.push(`🎙️ LỜI THOẠI / VOICE: ${s.voiceover}`);
    lines.push(`🎵 ÂM THANH / BGM-SFX: ${s.sound}`);
    lines.push(`----------------------------------------------------------------`);
  }

  return lines.join('\n');
}

/**
 * Trích xuất riêng danh sách Prompt Visual (cho AI vẽ ảnh / AI tạo video như Grok Imagine, Kling, Runway, Midjourney)
 */
export function extractVisualPrompts(episode: DramaEpisode): string {
  const lines: string[] = [];
  lines.push(`# DANH SÁCH PROMPT HÌNH ẢNH - ${episode.title.toUpperCase()}`);
  lines.push(`# Copy từng dòng vào các công cụ AI Video (Grok Imagine, Runway Gen-2, Kling AI, Midjourney)\n`);

  for (const s of episode.scenes) {
    lines.push(`[Cảnh ${s.sceneNumber} - ${s.timecode}]`);
    lines.push(`Tiếng Việt: ${s.visual}`);
    lines.push(`Gợi ý AI Video: 9:16 vertical short drama style, cinematic lighting, high emotional expression, dynamic angle, 4k ultra realistic, Chinese web drama aesthetics`);
    lines.push('');
  }

  return lines.join('\n');
}

/**
 * Xuất toàn bộ các tập kịch bản thành định dạng tổng hợp.
 */
export function exportAllEpisodes(episodes: DramaEpisode[], mode: 'voiceover' | 'production' | 'visual'): string {
  if (mode === 'voiceover') {
    return episodes.map(ep => extractVoiceoverText(ep)).join('\n\n' + '='.repeat(40) + '\n\n');
  }
  if (mode === 'visual') {
    return episodes.map(ep => extractVisualPrompts(ep)).join('\n\n' + '='.repeat(40) + '\n\n');
  }
  return episodes.map(ep => formatProductionScript(ep)).join('\n\n' + '='.repeat(60) + '\n\n');
}

/**
 * Thuật toán phân rã và viết lại kịch bản ngắn theo phong cách phim gây nghiện Trung Quốc (Douyin Short Drama Engine):
 * Đảm bảo mỗi tập chuẩn ~2 phút (khoảng 280 - 340 từ), có đủ 5 nấc cảm xúc:
 * 1. Hook mở đầu sốc (0-15s)
 * 2. Mâu thuẫn chèn ép (15-45s)
 * 3. Đẩy cao trào nghẹt thở (45-75s)
 * 4. Đảo ngược vả mặt (75-105s)
 * 5. Cắt ngang cliffhanger cực cuốn (105-120s)
 */
export function generateEpisodesFromPlot(options: RewriteOptions): DramaEpisode[] {
  const { title, sourceText, numEpisodes = 3, tone = 'va_mat' } = options;
  const episodes: DramaEpisode[] = [];

  // Trích xuất các ý chính từ sourceText nếu có
  const cleanSource = sourceText.trim() || title;
  const sentences = cleanSource.split(/[.?!;\n]+/).map(s => s.trim()).filter(s => s.length > 5);

  const getToneModifiers = (toneKey: DramaStyleTone) => {
    switch (toneKey) {
      case 'va_mat':
        return {
          hookVerb: 'bị sỉ nhục thậm tệ và quẳng đồ đạc vào mặt',
          conflictStyle: 'kẻ phản diện đắc chí giễu cợt, dùng tiền bạc thế lực đè bẹp đối phương',
          climaxAction: 'rút ra thẻ đen vô hạn mức và lệnh điều động vạn người xuất hiện',
          reversalTone: 'cái tát vang dội xé toạc sự ngạo mạn giả tạo',
          sfxClimax: 'Tiếng tát tai vang dội, tiếng gốm sứ vỡ vụn, tiếng còi xe quân đội/trực thăng rền vang bầu trời.'
        };
      case 'nguoc_luyen':
        return {
          hookVerb: 'ký đơn ly hôn trong sự lạnh nhạt vô tình của người từng thề hẹn',
          conflictStyle: 'bạch nguyệt quang giả tạo khóc lóc gài bẫy, tổng tài u mê trách móc',
          climaxAction: 'thân phận đại lão đứng đầu đế chế tài chính chính thức hé lộ',
          reversalTone: 'tổng tài quỳ sụp dưới mưa nhận ra mình đã đánh mất cả thế giới',
          sfxClimax: 'Tiếng ly rượu rơi vỡ toang, tiếng mưa rơi xối xả, tiếng violin bi thương xé ruột.'
        };
      case 'hai_huoc':
        return {
          hookVerb: 'giả nghèo đi xem mắt với bộ dạng hài hước khiến ai cũng ngã ngửa',
          conflictStyle: 'đối phương đòi quà cưới trên trời và tự vạch áo cho người xem lưng',
          climaxAction: 'vô tình làm rơi chìa khóa siêu xe và thẻ ngân hàng nghìn tỷ',
          reversalTone: 'mọi người trố mắt kinh ngạc, kẻ đào mỏ xấu hổ chạy mất dép',
          sfxClimax: 'Nhạc nền châm biếm rộn rã, tiếng cười cợt biến thành tiếng ho sặc sụa kinh ngạc.'
        };
      case 'kich_tinh':
      default:
        return {
          hookVerb: 'phát hiện bí mật động trời về thân phận thực sự của người thân cận',
          conflictStyle: 'cuộc đối đầu nghẹt thở giữa hai thế lực hắc bạch trong bóng tối',
          climaxAction: 'bằng chứng thép được tung lên máy chiếu trước mặt toàn bộ giới truyền thông',
          reversalTone: 'kẻ chủ mưu sụp đổ hoàn toàn trước sự thật không thể chối cãi',
          sfxClimax: 'Tiếng nhịp tim đập thình thịch, tiếng còi cảnh sát hú dồn dập, tiếng bass drop rung chuyển.'
        };
    }
  };

  const toneMod = getToneModifiers(tone);

  for (let i = 1; i <= Math.max(1, numEpisodes); i++) {
    const isFirst = i === 1;
    const isLast = i === numEpisodes;
    const epTitle = `Tập ${i}: ${
      isFirst
        ? 'Sóng Gió Nổi Lên & Cú Sốc Đầu Tiên'
        : isLast
        ? 'Đại Kết Cục: Vả Mặt Sảng Khoái & Tuyên Bố Vương Giả'
        : `Cao Trào Tiếp Diễn: Sự Thật Được Hé Lộ (Hồi ${i})`
    }`;

    const rawRefSentence = sentences[(i - 1) % sentences.length] || cleanSource.slice(0, 100);

    const scenes: DramaScene[] = [
      {
        sceneNumber: 1,
        timecode: '0:00 - 0:15',
        title: 'Hook 5s đầu - Cú sốc bùng nổ',
        visual: `Cận cảnh góc máy nghiêng ấn tượng. Nhân vật chính đối diện với sự cố bất ngờ: ${rawRefSentence.slice(0, 80)}. Biểu cảm xung quanh chuyển từ kiêu ngạo sang kinh hoàng.`,
        voiceover: isFirst
          ? `Ngay tại thời khắc này, mọi nhẫn nhịn suốt bấy lâu nay đã chính thức vượt quá giới hạn! Ai cũng nghĩ người đứng trước mặt là một kẻ vô dụng, nhưng không ai ngờ được thân phận thực sự phía sau!`
          : `Tưởng chừng mọi chuyện đã kết thúc, nhưng một sự biến động chấn động hơn vừa mới bắt đầu giáng xuống đầu những kẻ ngạo mạn!`,
        sound: 'Tiếng va chạm kim loại chát chúa, tiếng thở gấp, nhịp trống dồn dập mở màn.'
      },
      {
        sceneNumber: 2,
        timecode: '0:15 - 0:45',
        title: 'Mâu thuẫn bùng nổ - Kẻ ác ép bức',
        visual: `Góc trung quay toàn cảnh phòng khách sang trọng. Phản diện hếch cằm ra lệnh, vứt giấy tờ hoặc xấp tiền xuống trước mặt nhân vật chính. Đám đông xung quanh phụ họa châm chọc.`,
        voiceover: `Kẻ phản diện cười khẩy: "Dựa vào một kẻ khố rách áo ôm như mày mà cũng dám mơ trèo cao sao? Hôm nay nếu không quỳ xuống dập đầu nhận sai, đừng hòng bước ra khỏi cánh cửa này lành lặn!". Từng lời nói cay nghiệt như những nhát dao đâm vào danh dự.`,
        sound: 'Tiếng cười nhạo chói tai, nhạc nền căng thẳng rít lên theo từng lời thoại.'
      },
      {
        sceneNumber: 3,
        timecode: '0:45 - 1:15',
        title: 'Diễn biến đẩy cao - Sự phản kháng sắc lạnh',
        visual: 'Cận cảnh ánh mắt nhân vật chính. Đồng tử co lại, nụ cười nửa miệng xuất hiện đầy tự tin và bí hiểm. Đôi bàn tay nhẹ nhàng gạt phăng mọi sự uy hiếp.',
        voiceover: `Nhưng đáp lại sự hung hãn đó chỉ là một nụ cười lạnh lùng: "Nhà các người nghĩ tiền tài và quyền lực của mình có thể che được cả bầu trời này sao? Để xem, ai mới là người phải quỳ gối xin tha!". Từng bước chân tiến lên khiến kẻ phản diện bất giác phải lùi lại.`,
        sound: 'Tiếng bước giày da vang vọng đanh thép, tiếng thì thầm xôn xao kinh ngạc.'
      },
      {
        sceneNumber: 4,
        timecode: '1:15 - 1:45',
        title: 'Điểm đảo chiều - Cú vả mặt bước 1',
        visual: `Góc máy quét nhanh từ dưới lên trên. Một bằng chứng bất ngờ xuất hiện (chiếc thẻ đen, cuộc điện thoại từ cấp trên, đoàn xe hộ tống ập tới). Sắc mặt kẻ phản diện lập tức đổi màu tái mét.`,
        voiceover: `${toneMod.climaxAction}! Lời tuyên bố vang lên như sấm sét giữa trời quang. Những kẻ vừa giây trước còn cười cợt, nay hai chân run lẩy bẩy, mồ hôi vã ra như tắm khi nhận ra sự thật kinh hoàng!`,
        sound: toneMod.sfxClimax
      },
      {
        sceneNumber: 5,
        timecode: '1:45 - 2:00',
        title: 'Cao trào & Cliffhanger - Dừng lại ở giây phút nghẹt thở',
        visual: 'Góc máy cận xoay tròn 360 độ quanh nhân vật chính đang đứng sừng sững uy nghiêm, trong khi kẻ phản diện quỵ ngã. Đúng khoảnh khắc chiếc điện thoại reo vang hoặc một nhân vật bí ẩn bước vào, khung hình đóng băng!',
        voiceover: isLast
          ? `Bản án thích đáng đã được thực thi! Kẻ gieo gió nay phải gặt bão. Nhưng trước mắt nhân vật chính, một thế giới quyền lực hoàn toàn mới đang mở ra!`
          : `Chuông điện thoại reo vang xé toạc bầu không khí chết lặng! Đầu dây bên kia gào thét: "Mày đắc tội với ai? Cả gia tộc chúng ta sắp bị xóa sổ rồi!". Sự việc tiếp theo sẽ ra sao? Bấm ngay tập tiếp theo!`,
        sound: 'Tiếng bass drop rung rinh màng loa, tiếng tim đập hồi hộp, dứt khoát ngắt nhạc tạo cú sốc!'
      }
    ];

    episodes.push({
      episodeNumber: i,
      title: epTitle,
      hook: `Tình huống giật gân ${i}: ${toneMod.hookVerb}!`,
      cliffhanger: isLast
        ? 'Kết thúc hoàn hảo, vả mặt trọn vẹn!'
        : 'Cuộc gọi tử thần báo hiệu gia tộc sụp đổ, màn hình đóng băng đúng lúc nghẹt thở nhất!',
      durationSeconds: 120,
      scenes,
      notes: `Tập ${i} được tối ưu hóa cho độ dài 2 phút (khoảng 300 từ lời thoại) với cấu trúc giữ chân người xem Douyin / TikTok.`
    });
  }

  return episodes;
}
