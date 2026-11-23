import { NostalgiaScript, NostalgiaScene, NostalgiaCategoryKey } from '../data/nostalgiaScriptLibrary';

export interface CustomScriptOptions {
  userMemory: string;
  region: 'Bắc Bộ' | 'Miền Trung' | 'Miền Tây' | 'Chung';
  era: '8x' | '9x';
  mood: 'Nghẹn ngào rớt nước mắt' | 'Da diết nhớ cha mẹ' | 'Ấm áp hoài niệm' | 'Xót xa thương quê' | 'Hóm hỉnh ngây ngô';
  categoryKey?: NostalgiaCategoryKey;
}

/**
 * Trích xuất riêng văn bản lời bình (Voiceover) thuần túy
 * để dán thẳng vào CapCut, ElevenLabs, Vbee hoặc phần mềm đọc thoại tự động.
 */
export function extractNostalgiaVoiceover(script: NostalgiaScript): string {
  const lines: string[] = [];
  lines.push(`--- ${script.title.toUpperCase()} ---`);
  lines.push(`[Mở đầu - 0-5s]: ${script.hook}`);
  lines.push('');

  for (const s of script.scenes) {
    if (s.voiceover && s.voiceover.trim()) {
      lines.push(`[Phân cảnh ${s.sceneNumber} - ${s.timecode}]`);
      lines.push(s.voiceover.trim());
      lines.push('');
    }
  }

  lines.push(`[Lời kết lắng đọng]: ${script.endingNote}`);
  return lines.join('\n').trim();
}

/**
 * Xuất kịch bản sản xuất hoàn chỉnh: Phân cảnh chi tiết, thời gian, hình ảnh, lời dẫn, âm thanh.
 */
export function formatNostalgiaProductionScript(script: NostalgiaScript): string {
  const lines: string[] = [];
  lines.push(`================================================================`);
  lines.push(`🎬 KỊCH BẢN KÝ ỨC TUỔI THƠ: ${script.title.toUpperCase()}`);
  lines.push(`🏷️ Mã kịch bản: ${script.code} | Chủ đề: ${script.categoryName}`);
  lines.push(`🌾 Thời kỳ: ${script.era} | Vùng miền: ${script.region} | Cảm xúc: ${script.mood}`);
  lines.push(`⏱️ Thời lượng video: ~${Math.round(script.targetDurationSeconds / 60)} phút (${script.targetDurationSeconds} giây)`);
  lines.push(`🔥 Câu mở đầu Hook (0-5s): ${script.hook}`);
  lines.push(`💬 Lời kết lắng đọng: ${script.endingNote}`);
  lines.push(`================================================================\n`);

  for (const s of script.scenes) {
    lines.push(`[PHÂN CẢNH ${s.sceneNumber}] (${s.timecode}) - ${s.title}`);
    lines.push(`🎥 MÔ TẢ HÌNH ẢNH (BỐI CẢNH 8X 9X):`);
    lines.push(`   ${s.visualVi}`);
    lines.push(`🤖 MASTER PROMPT AI VIDEO (KLING / MIDJOURNEY / RUNWAY):`);
    lines.push(`   ${s.aiPromptEn}`);
    lines.push(`🎙️ LỜI BÌNH / VOICEOVER:`);
    lines.push(`   ${s.voiceover}`);
    lines.push(`🎵 ÂM THANH MÔI TRƯỜNG / SFX:`);
    lines.push(`   ${s.sound}`);
    lines.push(`📐 GÓC MÁY / CAMERA: ${s.camera}`);
    lines.push(`----------------------------------------------------------------`);
  }

  return lines.join('\n');
}

/**
 * Trích xuất danh sách Prompt chuẩn cho các công cụ AI Video (Kling AI, Midjourney, Hailuo, Runway Gen-3)
 */
export function extractNostalgiaAIPrompts(script: NostalgiaScript): string {
  const lines: string[] = [];
  lines.push(`# DANH SÁCH PROMPT AI VIDEO (KLING AI / MIDJOURNEY / HAILUO)`);
  lines.push(`# KỊCH BẢN: ${script.title.toUpperCase()} (MÃ: ${script.code})\n`);

  for (const s of script.scenes) {
    lines.push(`// --- CẢNH ${s.sceneNumber} (${s.timecode}): ${s.title} ---`);
    lines.push(`[Prompt Tiếng Anh Master]:`);
    lines.push(s.aiPromptEn);
    lines.push('');
    lines.push(`[Mô tả chi tiết Tiếng Việt tham khảo]:`);
    lines.push(s.visualVi);
    lines.push('');
  }

  return lines.join('\n');
}

/**
 * Sinh kịch bản Ký ức tuổi thơ tùy chỉnh từ ký ức cá nhân do người dùng nhập vào.
 * Tự động phân rã thành 5 phân cảnh chuẩn 2 phút, với mô tả hình ảnh cực kỳ chi tiết
 * và prompt AI tương thích.
 */
export function generateCustomChildhoodScript(options: CustomScriptOptions): NostalgiaScript {
  const { userMemory, region = 'Bắc Bộ', era = '8x', mood = 'Nghẹn ngào rớt nước mắt' } = options;
  const memoryClean = userMemory.trim() || 'Ký ức chiều mưa năm xưa cùng mẹ bên bếp củi';

  const title = `Ký Ức Tuổi Thơ: ${memoryClean.length > 40 ? memoryClean.slice(0, 40) + '...' : memoryClean}`;
  const code = `CUSTOM-${Date.now().toString().slice(-4)}`;

  const regionVisual = {
    'Bắc Bộ': 'căn nhà ngói ba gian hoặc vách đất trát rơm nứt nẻ, chum sành hứng nước mưa rêu phong, rặng tre già xào xạc trong gió bấc',
    'Miền Trung': 'căn nhà tranh vách đất ven cồn cát trắng, rặng phi lao vi vu gió Lào, hàng rào xương rồng rực rỡ trưa nắng',
    'Miền Tây': 'nhà sàn gỗ ven bến sông Cần Thơ, hàng dừa nước đu đưa, cầu khỉ lắc lẻo, chiếc ghe nan xuôi dòng đỏ ngầu phù sa',
    'Chung': 'ngôi nhà tranh vách đất mộc mạc bên đường đất đỏ đầy dấu chân trâu, cánh đồng lúa chín vàng ươm bát ngát',
  }[region];

  const scenes: NostalgiaScene[] = [
    {
      sceneNumber: 1,
      timecode: '0:00 - 0:15',
      title: 'Mở màn: Bức tranh làng quê ngày cũ',
      visualVi: `Cảnh góc rộng điện ảnh làng quê Việt Nam thập niên ${era === '8x' ? '1980' : '1990'}. Bối cảnh ${regionVisual}. Con đường đất sét gồ ghề trơn trượt in hằn những vũng nước mưa đục ngầu. Ánh hoàng hôn vàng vọt rọi qua kẽ lá, khói lam chiều bốc lên từ chái bếp tạo nên một bầu không khí hoài niệm sâu lắng đến nao lòng.`,
      aiPromptEn: `Cinematic 1990s rural Vietnam 35mm film still, Kodak Portra 400. Ultra-realistic authentic historical detail. Wide establishing shot of a quiet Vietnamese village: ${regionVisual}, muddy dirt road with puddles reflecting sunset sky, vintage thatched roof huts. Hazy golden hour dust motes, melancholic atmosphere, 8k resolution.`,
      voiceover: `Ai đi xa mà chẳng có một góc trời quê hương để nhớ. Nhớ những ngày tháng cơ hàn thiếu thốn nhưng đong đầy tình yêu thương: ${memoryClean}...`,
      sound: 'Tiếng gió chiều lay động cành tre xào xạc, tiếng chim chiều gọi bầy, tiếng bước chân trần lội bùn bì bõm',
      camera: 'slow wide cinematic establishing shot, eye level, hazy golden hour sun',
      durationSeconds: 15,
    },
    {
      sceneNumber: 2,
      timecode: '0:15 - 0:40',
      title: 'Hình bóng người thân và vật chứng thời gian',
      visualVi: `Cận cảnh nhân vật trong trang phục thời ${era}: áo may ô cháo lòng sứt chỉ thủng lỗ chỗ, quần đùi vải xanh cộc xắn cao, đôi dép tổ ong cũ mòn vẹt đế buộc bằng sợi dây dù. Đôi bàn chân trần đen nhẻm bấm sâu xuống bùn đất. Gương mặt gầy gò sạm đen vì nắng gió đồng ruộng, nụ cười sún răng ngây thơ trong trẻo rạng ngời.`,
      aiPromptEn: `Medium shot of an 8-year-old skinny Vietnamese child in rural 1990s. Wearing a worn-out faded ribbed singlet with tiny holes, rolled-up shorts, barefoot with muddy feet. Weathered sun-tanned skin, innocent sparkling eyes, rustic nostalgic textures, shallow depth of field, warm natural light.`,
      voiceover: `Ngày ấy làm gì có điện thoại, chẳng có đồ chơi xa xỉ. Niềm vui của lũ trẻ chúng tôi đơn sơ như củ sắn lùi tro, như que kem mút đổi bằng đôi dép đứt...`,
      sound: 'Tiếng dép lê lẹp xẹp trên đường đất, tiếng cười nói rộn rã của lũ trẻ thôn quê',
      camera: 'medium tracking shot, shallow depth of field, authentic 1990s Kodak film grain',
      durationSeconds: 25,
    },
    {
      sceneNumber: 3,
      timecode: '0:40 - 1:10',
      title: 'Khoảnh khắc xúc động bên gian nhà tranh',
      visualVi: `Cảnh trong gian bếp vách đất ám khói bồ hóng. Ánh sáng từ chiếc đèn dầu hỏa hoa kỳ le lói chiếu sáng chiếc chõng tre ọp ẹp. Người mẹ hoặc người cha lam lũ áo sờn vai vệt muối trắng, bàn tay gầy gò nứt nẻ gắp miếng thức ăn ngon nhất nhường cho con thơ. Bầu không khí ấm áp nghẹn ngào giữa cái nghèo cơ cực.`,
      aiPromptEn: `Emotional interior shot in a rustic clay hut in 1990s Vietnam. Flickering warm amber glow from a vintage glass kerosene lamp. Peasant mother with calloused hands lovingly sharing food with her child on a bamboo cot. Cracked mud walls, smoke haze, intense emotional depth.`,
      voiceover: `Mỗi lần nhớ về gian nhà tranh vách đất dột nát, nhớ dáng lưng còng của mẹ bên bếp lửa cay xè khói rơm, lòng con lại thắt nghẹn từng cơn...`,
      sound: 'Tiếng củi lách tách cháy trong bếp lò, tiếng gió lùa qua khe liếp, tiếng thở dài nhọc nhằn nhưng ấm áp',
      camera: 'slow push-in medium close-up, warm rustic indoor lighting, emotional depth',
      durationSeconds: 30,
    },
    {
      sceneNumber: 4,
      timecode: '1:10 - 1:40',
      title: 'Chi tiết cảm xúc: Đôi bàn tay hy sinh',
      visualVi: `Cận cảnh đôi bàn tay thô ráp chai sần nứt nẻ của cha mẹ đang chạm nhẹ vào đứa con nhỏ. Những đường gân guốc nổi rõ, móng tay bám phèn chua, từng vết sẹo của những mùa cấy gặt dầm mưa dãi nắng. Một giọt nước mắt lặng lẽ rơi xuống bàn tay gầy gò.`,
      aiPromptEn: `Extreme macro close-up of weathered, calloused hands of a Vietnamese peasant parent gently holding their child hand. Deep tactile skin wrinkles, mud stains under nails, poignant warm lighting, raw emotional reality.`,
      voiceover: `Cha mẹ đã đánh đổi cả thanh xuân, lấy giọt mồ hôi mặn chát trên đồng cạn đồng sâu để nâng niu từng giấc mơ con trẻ...`,
      sound: 'Tiếng nghẹn ngào khẽ nấc, tiếng côn trùng đêm hè nỉ non ngoài hiên',
      camera: 'tight close-up on weathered hands, high tactile realism',
      durationSeconds: 30,
    },
    {
      sceneNumber: 5,
      timecode: '1:40 - 2:00',
      title: 'Lắng đọng hoài niệm hoàng hôn quê hương',
      visualVi: `Cảnh góc rộng từ từ lùi xa (drone pull-back) nhìn toàn cảnh ngôi nhà tranh nép mình bên rặng tre và cánh đồng lúa chín vàng ươm bát ngát cò bay lả bay la. Hoàng hôn buông xuống nhuộm đỏ cả vòm trời miền quê. Cảm giác bình yên đến quặn thắt cõi lòng.`,
      aiPromptEn: `Cinematic slow drone pull-back wide shot of peaceful rural Vietnam landscape at twilight. A lone thatched cottage by endless golden ripe rice fields and meandering river. Nostalgic Kodak Portra film colors, poetic melancholic mood.`,
      voiceover: `Bây giờ lớn khôn bước ra ngoài xã hội, đi qua bao phố thị xa hoa, nhưng chẳng nơi đâu bình yên và ấm áp bằng góc quê nghèo thuở ấu thơ...`,
      sound: 'Tiếng chuông chùa xa xăm ngân nga, tiếng gió chiều xào xạc rặng tre',
      camera: 'slow cinematic pull-back wide shot, lingering sunset, melancholic mood',
      durationSeconds: 20,
    },
  ];

  return {
    id: `custom_${Date.now()}`,
    code,
    title,
    category: 'bua_com_ngheo',
    categoryName: 'Ký Ức Tuổi Thơ Tùy Chỉnh',
    era,
    region,
    mood,
    synopsis: `Kịch bản chuyển thể từ ký ức cá nhân: "${userMemory}".`,
    hook: `Ai đi xa mà chẳng có một góc trời quê hương để nhớ: ${memoryClean}...`,
    endingNote: 'Bao phố thị phồn hoa cũng chẳng thể ấm áp bằng góc quê nghèo thuở ấu thơ.',
    targetDurationSeconds: 120,
    scenes,
  };
}
