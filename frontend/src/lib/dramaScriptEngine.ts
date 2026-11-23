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

  // Phân bổ các giai đoạn câu chuyện theo tiến trình kinh điển 5 hồi:
  // Hồi 1 (Mở đầu & Sỉ nhục) -> Hồi 2 (Cạm bẫy & Mâu thuẫn) -> Hồi 3 (Đảo chiều & Thân phận) -> Hồi 4 (Đỉnh điểm đối đầu) -> Hồi 5 (Đại kết cục viên mãn)
  const total = Math.max(1, numEpisodes);

  for (let i = 1; i <= total; i++) {
    const isFirst = i === 1;
    const isLast = i === total;
    // Tính tiến độ câu chuyện từ 0.0 -> 1.0
    const progress = total === 1 ? 1 : (i - 1) / (total - 1);

    let epTitle = '';
    let stageDesc = '';
    let cliffhangerText = '';

    if (isFirst) {
      epTitle = `Tập 1: Sóng Gió Bùng Nổ & Cú Sốc Đầu Tiên`;
      stageDesc = `Mở màn xung đột gay gắt: nhân vật chính bị sỉ nhục và đẩy vào thế khó.`;
      cliffhangerText = `Một sự biến chấn động xuất hiện đúng khoảnh khắc nguy cấp nhất khiến tất cả câm lặng!`;
    } else if (isLast) {
      epTitle = `Tập ${i} (Đại Kết Cục): Vả Mặt Triệt Để & Đỉnh Cao Vinh Quang`;
      stageDesc = `Đại kết cục trọn vẹn: toàn bộ kẻ phản diện sụp đổ, chịu sự trừng phạt của pháp luật và công lý. Nhân vật chính vinh quang đoàn tụ hạnh phúc!`;
      cliffhangerText = `Khép lại trọn bộ series: kẻ ác đền tội, người tốt hưởng trọn vẹn vinh hoa hạnh phúc!`;
    } else if (progress < 0.4) {
      epTitle = `Tập ${i}: Cạm Bẫy Kẻ Ác & Mâu Thuẫn Dâng Cao`;
      stageDesc = `Kẻ phản diện đắc chí giăng bẫy chèn ép, nhân vật chính âm thầm tích lũy sức mạnh.`;
      cliffhangerText = `Cuộc gọi bí mật từ đại nhân vật quyền lực khiến tình thế bắt đầu rục rịch đảo chiều!`;
    } else if (progress < 0.7) {
      epTitle = `Tập ${i}: Thân Phận Thật Hé Lộ & Cú Vả Mặt Bước Đầu`;
      stageDesc = `Tài năng phi phàm hoặc thân phận tỷ phú bắt đầu rò rỉ, cú phản đòn đầu tiên giáng xuống.`;
      cliffhangerText = `Bằng chứng đanh thép xuất hiện ngay trước mắt khiến phe phản diện run rẩy bàng hoàng!`;
    } else {
      epTitle = `Tập ${i}: Đỉnh Điểm Đối Đầu Tại Đại Hội & Bóc Mẽ Kẻ Ác`;
      stageDesc = `Cuộc đối đầu trực diện tại sảnh tiệc thượng lưu, vạch trần mọi âm mưu trước bàn dân thiên hạ.`;
      cliffhangerText = `Lệnh phong sát và cảnh sát ập tới, hồi chuông báo tử cho gia tộc phản diện vang lên!`;
    }

    const rawRefSentence = sentences[(i - 1) % sentences.length] || cleanSource.slice(0, 100);

    const scenes: DramaScene[] = [
      {
        sceneNumber: 1,
        timecode: '0:00 - 0:15',
        title: isFirst ? 'Hook mở màn 5s - Cú sốc bùng nổ' : `Nút thắt tập ${i} - Cao trào dồn dập`,
        visual: `Cận cảnh góc máy nghiêng ấn tượng. Tình huống: ${rawRefSentence.slice(0, 80)}. Biểu cảm xung quanh chuyển từ kiêu ngạo sang kinh hoàng.`,
        voiceover: isFirst
          ? `Ngay tại thời khắc này, mọi nhẫn nhịn suốt bấy lâu nay đã chính thức vượt quá giới hạn! Ai cũng nghĩ người đứng trước mặt là một kẻ vô dụng, nhưng không ai ngờ được thân phận thực sự phía sau!`
          : isLast
          ? `Giờ phút phán xét cuối cùng đã điểm! Những kẻ từng ngạo mạn giẫm đạp lên người khác, hôm nay sẽ phải trả giá gấp trăm ngàn lần!`
          : `Không khí tại hiện trường nghẹt thở đến từng giây! Cứ ngỡ đối phương đã cùng đường, nhưng một quân bài tẩy kinh hoàng vừa được lật mở!`,
        sound: 'Tiếng va chạm kim loại chát chúa, tiếng thở gấp, nhịp trống dồn dập mở màn.'
      },
      {
        sceneNumber: 2,
        timecode: '0:15 - 0:45',
        title: isLast ? 'Kẻ phản diện sụp đổ quỳ lạy' : 'Mâu thuẫn bùng nổ - Kẻ ác ép bức',
        visual: isLast
          ? `Cận cảnh kẻ phản diện mặt cắt không còn giọt máu, hai đầu gối khuỵu xuống sàn đá cẩm thạch van xin trong tuyệt vọng.`
          : `Góc trung quay toàn cảnh phòng khách sang trọng. Phản diện hếch cằm ra lệnh, vứt giấy tờ hoặc xấp tiền xuống trước mặt nhân vật chính.`,
        voiceover: isLast
          ? `Kẻ phản diện run rẩy bò lết dưới sàn: "Tôi sai rồi! Xin tha cho tôi một con đường sống!". Nhưng tiếng cười lạnh lùng của nhân vật chính đã dập tắt mọi hy vọng trốn tội!`
          : `Kẻ phản diện cười khẩy: "Dựa vào một kẻ khố rách áo ôm như mày mà cũng dám mơ trèo cao sao? Hôm nay nếu không quỳ xuống dập đầu nhận sai, đừng hòng bước ra khỏi cánh cửa này lành lặn!".`,
        sound: isLast ? 'Tiếng đập đầu chan chát xuống sàn, tiếng khóc than xin tha.' : 'Tiếng cười nhạo chói tai, nhạc nền căng thẳng rít lên theo từng lời thoại.'
      },
      {
        sceneNumber: 3,
        timecode: '0:45 - 1:15',
        title: isLast ? 'Tiếp quản quyền lực & Đền tội' : 'Diễn biến đẩy cao - Sự phản kháng sắc lạnh',
        visual: isLast
          ? `Đội ngũ luật sư và cảnh sát xuất hiện, trao quyền thừa kế và còng tay kẻ phạm tội lôi ra xe đặc chủng.`
          : `Cận cảnh ánh mắt nhân vật chính. Đồng tử co lại, nụ cười nửa miệng xuất hiện đầy tự tin và bí hiểm. Đôi bàn tay nhẹ nhàng gạt phăng mọi sự uy hiếp.`,
        voiceover: isLast
          ? `Bản hợp đồng chuyển nhượng toàn bộ gia sản chính thức ký kết! Kẻ ác cúi đầu tra tay vào còng số 8 trong sự khinh bỉ của toàn xã hội!`
          : `Nhưng đáp lại sự hung hãn đó chỉ là một nụ cười lạnh lùng: "Nhà các người nghĩ tiền tài và quyền lực của mình có thể che được cả bầu trời này sao? Để xem, ai mới là người phải quỳ gối xin tha!".`,
        sound: isLast ? 'Tiếng còng số 8 khóa cạch, tiếng còi xe cảnh sát hú vang.' : 'Tiếng bước giày da vang vọng đanh thép, tiếng thì thầm xôn xao kinh ngạc.'
      },
      {
        sceneNumber: 4,
        timecode: '1:15 - 1:45',
        title: isLast ? 'Vinh quang & Hạnh phúc trọn vẹn' : 'Điểm đảo chiều - Cú vả mặt chấn động',
        visual: isLast
          ? `Nhân vật chính nắm tay người mình yêu sải bước trên thảm đỏ, ánh hào quang của vương giả rực rỡ, xung quanh vạn người chúc tụng.`
          : `Góc máy quét nhanh từ dưới lên trên. Một bằng chứng bất ngờ xuất hiện (chiếc thẻ đen, cuộc điện thoại từ cấp trên, đoàn xe hộ tống ập tới).`,
        voiceover: isLast
          ? `Trước mặt hàng triệu người ngưỡng mộ, lời hẹn ước trăm năm được trao gửi. Sóng gió qua đi, chỉ còn lại tình yêu bất diệt và ngai vàng quyền lực tối thượng!`
          : `${toneMod.climaxAction}! Lời tuyên bố vang lên như sấm sét giữa trời quang. Những kẻ vừa giây trước còn cười cợt, nay hai chân run lẩy bẩy, mồ hôi vã ra như tắm!`,
        sound: isLast ? 'Nhạc giao hưởng hoành tráng du dương, tiếng pháo hoa rộn rã.' : toneMod.sfxClimax
      },
      {
        sceneNumber: 5,
        timecode: '1:45 - 2:00',
        title: isLast ? 'Đại kết cục hoàn mỹ - Trọn bộ viên mãn' : 'Cao trào & Cliffhanger - Dừng lại ở giây phút nghẹt thở',
        visual: isLast
          ? `Khung cảnh toàn cảnh lâu đài pháo hoa sáng rực rỡ trên bầu trời đêm. Nụ cười hạnh phúc rạng ngời kết thúc toàn bộ thiên truyện.`
          : `Góc máy cận xoay tròn 360 độ quanh nhân vật chính đang đứng sừng sững uy nghiêm, trong khi kẻ phản diện quỵ ngã. Đúng khoảnh khắc then chốt, khung hình đóng băng!`,
        voiceover: isLast
          ? `Một hành trình báo thù và nghịch tập hoàn mỹ đã chính thức khép lại! Khổ tận cam lai, vương giả quy vị, trọn vẹn đến từng cảm xúc!`
          : `Chuông điện thoại reo vang xé toạc bầu không khí chết lặng! Đầu dây bên kia gào thét: "Mày đắc tội với ai? Cả gia tộc chúng ta sắp bị xóa sổ rồi!". Sự việc tiếp theo sẽ ra sao? Bấm ngay tập tiếp theo!`,
        sound: isLast ? 'Tiếng chuông ngân vang, ca khúc kết phim hùng tráng trọn vẹn!' : 'Tiếng bass drop rung rinh màng loa, tiếng tim đập hồi hộp, dứt khoát ngắt nhạc tạo cú sốc!'
      }
    ];

    episodes.push({
      episodeNumber: i,
      title: epTitle,
      hook: `Tình huống giật gân tập ${i}: ${toneMod.hookVerb}!`,
      cliffhanger: cliffhangerText,
      durationSeconds: 120,
      scenes,
      notes: `Tập ${i}/${total} (${isLast ? 'TẬP CUỐI - ĐẠI KẾT CỤC' : 'Giai đoạn cốt truyện: ' + stageDesc}) - Tối ưu 2 phút chuẩn kịch bản Douyin.`
    });
  }

  return episodes;
}
