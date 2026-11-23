import { DramaEpisode, DramaScene } from '../data/chineseDramaLibrary';

export type DramaStyleTone = 'va_mat' | 'kich_tinh' | 'nguoc_luyen' | 'hai_huoc';
export type VietnameseSettingOption = 'mien_tay' | 'sai_gon' | 'dien_chu_bac_lieu' | 'generic_vn';

export interface RewriteOptions {
  title: string;
  sourceText: string;
  numEpisodes: number;
  targetDurationSeconds?: number; // default 120 (2 minutes)
  tone?: DramaStyleTone;
  vietnameseSetting?: VietnameseSettingOption;
  includeVisualPrompts?: boolean;
  includeSoundEffects?: boolean;
}

export interface StudioShotExport {
  durationSeconds: number;
  action: string;
  camera: string;
  speaker: string;
  dialogue: string;
  sound: string;
  cast: string[];
  location?: string;
}

/**
 * Chuyển đổi các phân cảnh của tập phim thành danh sách Shot chuẩn của Studio Video AI (text-to-video + giọng đọc AI).
 * Không gán id: server tạo id khi lưu kế hoạch. Thời lượng đủ để đọc câu thoại (~3 chữ/giây + 1s), trong giới hạn 2–12s
 * của Seedance. Góc máy viết bằng tiếng Anh, ưu tiên khung rộng vì giọng được lồng sau (không cận miệng).
 */
export function convertScenesToStudioShots(scenes: DramaScene[]): StudioShotExport[] {
  return scenes.map((s, idx) => {
    let speaker = 'Lời dẫn';
    let dialogue = s.voiceover;
    const match = /^([^:：]{2,25})[:：]\s*(.+)$/s.exec(s.voiceover);
    if (match) {
      speaker = match[1].trim();
      dialogue = match[2].trim();
    }
    const words = dialogue.trim().split(/\s+/).filter(Boolean).length;
    return {
      durationSeconds: words > 0 ? Math.min(12, Math.max(2, Math.ceil(words / 3 + 1))) : 5,
      action: s.visual,
      camera: idx === 0 ? 'wide establishing shot' : idx === scenes.length - 1 ? 'wide shot, slow drone pull-back' : 'medium-wide shot',
      speaker,
      dialogue,
      sound: s.sound,
      cast: speaker === 'Lời dẫn' ? [] : [speaker],
    };
  });
}

/**
 * Ước tính thời gian đọc lời thoại tiếng Việt:
 * Trung bình 2.5 - 3 từ/giây khi đọc biểu cảm kịch tính + 1.5s mỗi lần đổi cảnh/dừng nghỉ.
 */
export function estimateReadingTime(text: string): { words: number; seconds: number; formatted: string } {
  const clean = text.replace(/\[[^\]]*\]/g, ' ').replace(/[*_`>#]/g, ' ').trim();
  const words = clean.split(/\s+/).filter(Boolean).length;
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
  if (episode.cliffhanger) lines.push(`⚡ Cliffhanger nối tập: ${episode.cliffhanger}`);
  lines.push(`================================================================\n`);

  for (const s of episode.scenes) {
    lines.push(`[PHÂN CẢNH ${s.sceneNumber}] (${s.timecode}) - ${s.title}`);
    lines.push(`🎥 HÌNH ẢNH / BỐI CẢNH VN: ${s.visual}`);
    lines.push(`🎙️ LỜI THOẠI / VOICE: ${s.voiceover}`);
    lines.push(`🎵 ÂM THANH / SFX: ${s.sound}`);
    lines.push(`----------------------------------------------------------------`);
  }

  return lines.join('\n');
}

/**
 * Trích xuất riêng danh sách Prompt Visual (cho AI vẽ ảnh / AI tạo video bối cảnh Việt Nam / Miền Tây sông nước)
 */
export function extractVisualPrompts(episode: DramaEpisode): string {
  const lines: string[] = [];
  lines.push(`# DANH SÁCH PROMPT HÌNH ẢNH (BỐI CẢNH VIỆT NAM) - ${episode.title.toUpperCase()}`);
  lines.push(`# Copy từng dòng vào các công cụ AI Video (Grok Imagine, Runway Gen-2, Kling AI, Midjourney)\n`);

  for (const s of episode.scenes) {
    lines.push(`[Cảnh ${s.sceneNumber} - ${s.timecode}]`);
    lines.push(`Tiếng Việt: ${s.visual}`);
    lines.push(`AI Prompt: 9:16 vertical short drama cinematic shot, Vietnam Mekong river delta / Saigon scenery, Vietnamese actors with intense emotional facial expressions, dramatic lighting, 4k ultra realistic`);
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
 * Thuật toán phân rã và viết lại kịch bản ngắn bối cảnh Việt Nam (Miền Tây sông nước / Đô thị Sài Gòn):
 * - Đảm bảo mỗi tập chuẩn ~2 phút (~300 từ lời đọc lồng tiếng).
 * - Cốt truyện 5 hồi liên kết chặt chẽ (Tập 1 kết đâu -> Tập 2 mở màn đúng câu đó).
 * - Nhân vật, địa danh, câu thoại thuần phong mỹ tục Việt Nam.
 */
export function generateEpisodesFromPlot(options: RewriteOptions): DramaEpisode[] {
  const { title, sourceText, numEpisodes = 3, tone = 'va_mat', vietnameseSetting = 'mien_tay' } = options;
  const episodes: DramaEpisode[] = [];

  const cleanSource = sourceText.trim() || title;
  const sentences = cleanSource.split(/[.?!;\n]+/).map(s => s.trim()).filter(s => s.length > 5);

  const getToneModifiers = (toneKey: DramaStyleTone) => {
    switch (toneKey) {
      case 'va_mat':
        return {
          hookVerb: 'bị má vợ hắt xô nước rửa chén sỉ nhục đuổi khỏi bến đò',
          conflictStyle: 'gã thiếu gia hách dịch vứt xấp tiền cọc đòi ép gả người yêu',
          climaxAction: 'đoàn cano vũ trang cùng trực thăng xé nước sông Hậu đổ bộ cứu giá',
          reversalTone: 'cái tát vang trời giáng xuống mặt kẻ kiêu ngạo',
          sfxClimax: 'Tiếng tát tai vang dội, tiếng còi cano báo động, tiếng trực thăng gầm rú rung chuyển bến sông.'
        };
      case 'nguoc_luyen':
        return {
          hookVerb: 'ký đơn ly hôn dứt áo ra đi trong cơn mưa dông bên bờ sông',
          conflictStyle: 'ả trà xanh con nuôi giả khóc gài bẫy, chồng cũ u mê trách móc',
          climaxAction: 'thân phận bà trùm chuỗi vựa lúa xuất khẩu chính thức lộ diện',
          reversalTone: 'chồng cũ dầm mưa quỳ gối xin tha thứ nhưng nhận lại sự lạnh lùng',
          sfxClimax: 'Tiếng ly trà rơi vỡ tan tành, tiếng mưa dông quất vào rặng dừa, tiếng vĩ cầm xé ruột.'
        };
      case 'hai_huoc':
        return {
          hookVerb: 'chạy xe ôm cà tàng đi coi mắt bị chê nghèo hắt ly nước mía',
          conflictStyle: 'đối phương đòi quà cưới 5 tỷ và đòi sang tên sổ đỏ miệt vườn',
          climaxAction: 'làm rơi chìa khóa du thuyền triệu đô và thẻ ngân hàng vạn tỷ',
          reversalTone: 'cả gia đình đối phương há hốc mồm, cô gái xấu hổ trốn mất dép',
          sfxClimax: 'Nhạc nền châm biếm rộn rã, tiếng cười cợt chuyển sang tiếng sặc nước kinh ngạc.'
        };
      case 'kich_tinh':
      default:
        return {
          hookVerb: 'phát hiện bí mật động trời về nguồn gốc người thừa kế gia tộc',
          conflictStyle: 'cuộc đấu trí nghẹt thở tại tiệc gia tộc giữa các phe cánh điền chủ',
          climaxAction: 'bản kết luận giám định pháp y ADN mộc đỏ được tung ra ngay bàn thờ tổ',
          reversalTone: 'kẻ chủ mưu sụp đổ hoàn toàn trước sự thật không thể chối cãi',
          sfxClimax: 'Tiếng còi cảnh sát hú dồn dập trên cầu sông Hậu, tiếng trống trận rền vang.'
        };
    }
  };

  const toneMod = getToneModifiers(tone);
  const total = Math.max(1, numEpisodes);

  for (let i = 1; i <= total; i++) {
    const isFirst = i === 1;
    const isLast = i === total;
    const progress = total === 1 ? 1 : (i - 1) / (total - 1);

    let epTitle = '';
    let stageDesc = '';
    let cliffhangerText = '';

    if (isFirst) {
      epTitle = `Tập 1: Sóng Gió Nổi Lên & Nút Thắt Đầu Tiên`;
      stageDesc = `Mở màn xung đột: nhân vật chính bị sỉ nhục bên bến sông, giọt nước tràn ly.`;
      cliffhangerText = `Tiếng động cơ gầm vang xé toạc màn sương ngã ba sông, lực lượng quân đoàn bất ngờ xuất hiện bến đò!`;
    } else if (isLast) {
      epTitle = `Tập ${i} (Đại Kết Cục): Vả Mặt Triệt Để & Đám Cưới Thế Kỷ Bên Sông`;
      stageDesc = `Đại kết cục: kẻ phản diện phá sản quỳ lạy, công lý thực thi, rước dâu bằng du thuyền 5 sao!`;
      cliffhangerText = `Khép lại trọn bộ series: kẻ ác đền tội, người tốt hưởng vinh hoa hạnh phúc trọn đời!`;
    } else if (progress < 0.4) {
      epTitle = `Tập ${i}: Kẻ Ác Giăng Bẫy & Mâu Thuẫn Dâng Cao`;
      stageDesc = `Thiếu gia và má vợ dùng tiền tài ép vào đường cùng, nhân vật chính bắt đầu phản kháng.`;
      cliffhangerText = `Cuộc gọi từ tập đoàn mẹ báo tin khẩn cấp: "Tập đoàn nhà chúng ta sắp phá sản rồi!"`;
    } else if (progress < 0.7) {
      epTitle = `Tập ${i}: Thân Phận Đại Lão Hé Lộ & Cú Vả Mặt Bước Đầu`;
      stageDesc = `Thẻ đen trăm tỷ xuất hiện, vạch trần bộ mặt tham lam của kẻ phản diện.`;
      cliffhangerText = `Bằng chứng pháp y ADN và hồ sơ điều tra được tung lên khiến cả dòng họ chấn động!`;
    } else {
      epTitle = `Tập ${i}: Đỉnh Điểm Đối Đầu Tại Đại Tiệc Hào Môn`;
      stageDesc = `Cuộc chạm trán nảy lửa trước mặt toàn bộ giới làm ăn Nam Bộ, lật mặt kẻ phản đồ.`;
      cliffhangerText = `Cảnh sát hình sự ập vào bến tàu còng tay kẻ chủ mưu áp giải lên xe đặc chủng!`;
    }

    const rawRefSentence = sentences[(i - 1) % sentences.length] || cleanSource.slice(0, 100);

    const scenes: DramaScene[] = [
      {
        sceneNumber: 1,
        timecode: '0:00 - 0:15',
        title: isFirst ? 'Hook mở màn 5s - Bị sỉ nhục bến đò' : `Nối tiếp tập ${i - 1} - Đòn phản công bất ngờ`,
        visual: isFirst
          ? `Góc máy nghiêng quay cận cảnh bến đò miền Tây sông nước. Nhân vật chính bị xô ngã, xô nước bẩn hắt tung tóe lên đôi ủng cũ: ${rawRefSentence.slice(0, 80)}. Xung quanh họ hàng cười nhạo.`
          : `Góc máy nối tiếp ngay khoảnh khắc tập trước. Vị tướng lĩnh hoặc đại lão bước lên cầu tàu gỗ, quỳ một gối trước mặt nhân vật chính giữa sự bàng hoàng của cả xóm làng.`,
        voiceover: isFirst
          ? `Ba năm làm rể miệt vườn, cắn răng nhẫn nhục che chở gia đình. Nhưng hôm nay, những kẻ tham tiền đã đẩy mọi chuyện vượt quá giới hạn chịu đựng!`
          : isLast
          ? `Giờ phút đền tội cuối cùng đã điểm! Những kẻ từng đem tiền ra chà đạp danh dự người nghèo, hôm nay phải trả giá gấp trăm ngàn lần!`
          : `Tiếng xôn xao chết lặng cả khúc sông! Không ai ngờ được người đàn ông nghèo hèn bấy lâu nay lại nắm giữ sinh mệnh kinh tế của cả một vùng đất!`,
        sound: isFirst ? 'Tiếng nước sông vỗ bì bõm, tiếng xô nhựa đập chát chúa, nhạc u ám.' : 'Tiếng bốt da nện trên ván gỗ rầm rập, tiếng hô nghiêm trang.'
      },
      {
        sceneNumber: 2,
        timecode: '0:15 - 0:45',
        title: isLast ? 'Kẻ phản diện quỳ lạy xin tha' : 'Mâu thuẫn leo thang - Kẻ ác ép bức',
        visual: isLast
          ? `Thiếu gia và má vợ phản diện mặt cắt không còn giọt máu, quỳ sụp dưới sình lầy dập đầu van xin.`
          : `Góc trung trong sảnh nhà cổ miền Tây. Phản diện vứt xấp tiền hoặc hợp đồng xuống đất, hếch cằm ra lệnh trịch thượng.`,
        voiceover: isLast
          ? `Kẻ phản diện ôm chân khóc ròng: "Tôi sai rồi! Xin Chủ Tịch mở lượng hải hà tha cho gia tộc tôi!". Nhưng tiếng cười sắc lạnh đã dập tắt mọi hy vọng.`
          : `Kẻ phản diện cười khẩy: "Dựa vào thứ khố rách áo ôm như mày mà đòi cưới tiểu thư sao? Không quỳ xuống dập đầu nhận lỗi thì đừng hòng rời khỏi bến sông này lành lặn!".`,
        sound: isLast ? 'Tiếng đập đầu chan chát xuống sàn, tiếng khóc than xin tha.' : 'Tiếng cười nhạo chói tai, nhạc nền dồn dập gay cấn.'
      },
      {
        sceneNumber: 3,
        timecode: '0:45 - 1:15',
        title: isLast ? 'Tiếp quản quyền lực & Tra tay vào còng' : 'Diễn biến đẩy cao - Sự phản kháng sắc bén',
        visual: isLast
          ? `Công an kinh tế và kiểm sát viên xuất hiện niêm phong toàn bộ tài sản bất chính của kẻ phản diện.`
          : `Cận cảnh ánh mắt nhân vật chính. Nụ cười nửa miệng đầy khí phách xuất hiện. Đôi bàn tay nhẹ nhàng xé toạc mọi tờ giấy ép bức.`,
        voiceover: isLast
          ? `Bản án thích đáng đã được thực thi! Kẻ ác cúi đầu tra tay vào còng số 8 trong sự khinh bỉ của toàn thể bà con lối xóm!`
          : `Nhưng đáp lại chỉ là sự khinh miệt lạnh lùng: "Nhà các người nghĩ mấy đồng tiền mỏ cát có thể che được bầu trời này sao? Để xem, ai mới là người phải quỳ lạy xin tha!".`,
        sound: isLast ? 'Tiếng còng số 8 khóa cạch, tiếng còi xe cảnh sát hú vang.' : 'Tiếng bước chân đanh thép, tiếng thở dốc bàng hoàng.'
      },
      {
        sceneNumber: 4,
        timecode: '1:15 - 1:45',
        title: isLast ? 'Vinh quang & Hạnh phúc trọn vẹn' : 'Điểm đảo chiều - Cú vả mặt sấm sét',
        visual: isLast
          ? `Nhân vật chính nắm tay người yêu bước lên du thuyền 5 sao, pháo hoa rực sáng trên bầu trời sông quê.`
          : `Góc máy quét nhanh từ dưới lên trên. Chiếc thẻ đen trăm tỷ rơi keng xuống mâm đồng, hoặc lệnh triệu tập hạm đội tàu ập tới.`,
        voiceover: isLast
          ? `Trước mặt hàng ngàn người ngưỡng mộ, nhẫn kim cương được trao tay. Khổ tận cam lai, chỉ còn lại tình yêu bất diệt và hạnh phúc viên mãn trọn đời!`
          : `${toneMod.climaxAction}! Lời tuyên bố vang lên như sấm sét giữa trời quang. Kẻ kiêu ngạo nay hai chân run lẩy bẩy, mồ hôi vã ra như tắm!`,
        sound: isLast ? 'Nhạc đờn ca giao hưởng hùng tráng xúc động, tiếng pháo hoa rộn rã.' : toneMod.sfxClimax
      },
      {
        sceneNumber: 5,
        timecode: '1:45 - 2:00',
        title: isLast ? 'Đại kết cục viên mãn' : 'Cao trào & Cliffhanger nối tập',
        visual: isLast
          ? `Toàn cảnh bến đò sông nước rực rỡ cờ hoa và ánh bình minh. Nụ cười hạnh phúc rạng ngời khép lại bộ phim.`
          : `Cận cảnh 360 độ xoay quanh nhân vật chính uy nghi đứng giữa sảnh đường, điện thoại kẻ phản diện réo vang, khung hình đóng băng đúng tích tắc gay cấn nhất!`,
        voiceover: isLast
          ? `Một thiên truyện nghịch tập miền Tây trọn vẹn và hoàn mỹ! Ở hiền gặp lành, kẻ ác gieo gió gặt bão, vương giả quy vị đắc nhân tâm!`
          : `Chuông điện thoại réo vang xé toạc bầu không khí chết lặng! Đầu dây bên kia gào thét: "Mày đắc tội với ai? Cả nhà mình sắp phá sản rồi!". Chuyện gì sẽ xảy ra? Xem ngay tập tiếp theo!`,
        sound: isLast ? 'Tiếng chuông ngân vang, khúc ca khép lại trọn bộ series đầy thỏa mãn!' : 'Tiếng bass drop rung chuyển, tiếng tim đập hồi hộp ngắt tập!'
      }
    ];

    episodes.push({
      episodeNumber: i,
      title: epTitle,
      hook: `Tình huống giật gân tập ${i}: ${toneMod.hookVerb}!`,
      cliffhanger: cliffhangerText,
      durationSeconds: 120,
      scenes,
      notes: `Tập ${i}/${total} (${isLast ? 'TẬP CUỐI - ĐẠI KẾT CỤC' : 'Giai đoạn: ' + stageDesc}) - Chuẩn 2 phút lồng tiếng Việt, bối cảnh sông nước Nam Bộ.`
    });
  }

  return episodes;
}
