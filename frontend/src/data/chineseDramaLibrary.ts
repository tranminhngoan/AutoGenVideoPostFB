export interface DramaCharacter {
  name: string;
  role: string; // e.g. "Nam chính", "Nữ chính", "Mẹ vợ phản diện", "Trà xanh"
  description: string;
}

export interface DramaScene {
  sceneNumber: number;
  timecode: string; // e.g. "0:00 - 0:15"
  title: string;
  visual: string; // Prompt / mô tả hình ảnh cho AI video / quay dựng
  voiceover: string; // Lời dẫn / lời thoại lồng tiếng
  sound: string; // SFX, BGM, cảm xúc âm thanh
}

export interface DramaEpisode {
  episodeNumber: number;
  title: string;
  hook: string; // Điểm giật gân mở đầu (5s)
  cliffhanger: string; // Cú lật / cắt tập gay cấn
  durationSeconds: number; // ~120s (2 phút)
  scenes: DramaScene[];
  notes?: string;
}

export interface ChineseDrama {
  id: string;
  title: string;
  originalTitle: string; // Tên gốc tiếng Trung (Pinyin / Hán tự)
  genre: string;
  genreKey: 'zhan_shen' | 'tong_tai' | 'trung_sinh' | 'manh_bao' | 'gia_ngheo' | 'xuyen_khong' | 'nguoc_luyen' | 'than_y' | 'doan_sung';
  tags: string[];
  addictionRating: number; // 4.8 - 5.0
  viewsEstimate: string; // e.g. "1.2 Tỷ lượt xem Douyin"
  hook: string;
  synopsis: string;
  characters: DramaCharacter[];
  viralHighlights: string[];
  suggestedEpisodes: number;
  isFullSeries: boolean; // Đánh dấu đã có trọn bộ cốt truyện từ Mở đầu đến Đại Kết Cục
  episodes: DramaEpisode[];
}

export const DRAMA_GENRES = [
  { key: 'all', label: 'Tất cả thể loại' },
  { key: 'zhan_shen', label: 'Chiến Thần / Ở Rể' },
  { key: 'tong_tai', label: 'Tổng Tài / Hào Môn' },
  { key: 'trung_sinh', label: 'Trùng Sinh / Báo Thù' },
  { key: 'manh_bao', label: 'Mẹ Kế / Manh Bảo' },
  { key: 'gia_ngheo', label: 'Giả Nghèo / Xem Mắt' },
  { key: 'nguoc_luyen', label: 'Vợ Cũ Đại Lão / Ngược' },
  { key: 'xuyen_khong', label: 'Xuyên Không / Nữ Cường' },
  { key: 'than_y', label: 'Thần Y / Dị Năng' },
  { key: 'doan_sung', label: 'Đoàn Sủng / Thiên Kim' },
] as const;

export const CHINESE_DRAMAS: ChineseDrama[] = [
  // 1. CHIẾN THẦN Ở RỂ (TRỌN BỘ 3 TẬP CHUẨN 2 PHÚT)
  {
    id: 'zhan-shen-long-te',
    title: 'Chiến Thần Ở Rể: 100 Tỷ Đô Xuất Kích',
    originalTitle: '战神龙婿: 赘婿逆袭 (Chiến Thần Long Tế)',
    genre: 'Chiến Thần / Ở Rể',
    genreKey: 'zhan_shen',
    tags: ['Vả mặt', 'Ở rể', 'Thân phận thật', 'Long Vương', 'Trọn bộ Full Arc'],
    addictionRating: 5.0,
    viewsEstimate: '2.4 Tỷ lượt xem Douyin',
    hook: 'Làm rể 3 năm quỳ rửa chân cho mẹ vợ, ngày thượng thọ bị đuổi ra đường, 10 chiếc trực thăng quân đoàn bất ngờ hạ cánh!',
    synopsis: 'Diệp Thần vì lời thề với sư phụ mà ẩn thân làm rể hèn nhà họ Lâm suốt 3 năm, chịu đủ trăm bề nhục nhã. Đúng ngày đại thọ 70 của lão thái thái, mẹ vợ ép anh ký đơn ly hôn để gả vợ anh cho thiếu gia tập đoàn họ Vương. Khi hợp đồng hôn nhân vừa xé, 10 chiếc trực thăng vũ trang của Chiến Thần Điện bất ngờ phủ kín bầu trời thành phố, vạn binh sĩ quỳ rạp nghênh đón Long Vương quy vị, trừng phạt kẻ bạc ác và đưa vợ yêu lên đỉnh cao vinh quang!',
    characters: [
      { name: 'Diệp Thần', role: 'Nam chính', description: 'Long Vương Chiến Thần thống lĩnh 10 vạn thiết vệ, giả nghèo làm rể để báo ân.' },
      { name: 'Lâm Thanh Uyển', role: 'Nữ chính', description: 'Vợ Diệp Thần, xinh đẹp nhân hậu nhưng bị gia tộc chèn ép buộc phải tái giá.' },
      { name: 'Bà Triệu', role: 'Mẹ vợ phản diện', description: 'Hám tiền, cay nghiệt, luôn tìm cách xua đuổi con rể để đón thiếu gia giàu có.' },
      { name: 'Vương Khắc', role: 'Thiếu gia phản diện', description: 'Công tử tập đoàn Vương Thị, hống hách, dùng tiền ép bức nhà họ Lâm.' },
    ],
    viralHighlights: [
      'Cảnh xé hợp đồng ly hôn và ném xuống mặt thiếu gia nhà giàu.',
      '10 chiếc trực thăng rợp trời, cận cảnh mặt mẹ vợ tái mét không còn giọt máu.',
      'Chiếc thẻ đen Black Card vô hạn mức ném thẳng vào bàn tiệc.',
    ],
    suggestedEpisodes: 3,
    isFullSeries: true,
    episodes: [
      {
        episodeNumber: 1,
        title: 'Tập 1: Nhục Nhã Ngày Thượng Thọ & Cú Lật Rợp Trời',
        hook: 'Mẹ vợ hất bát nước rửa chân vào mặt Diệp Thần ngay trước mặt toàn bộ khách khứa dự tiệc!',
        cliffhanger: 'Tiếng động cơ trực thăng gầm rú xé toạc bầu trời, đoàn vệ sĩ mặc áo giáp rồng xông vào sảnh tiệc!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Hook mở màn - Sỉ nhục tột cùng',
            visual: 'Cận cảnh bát nước bẩn văng tung tóe lên đôi giày cũ của Diệp Thần. Mẹ vợ chỉ thẳng tay vào trán anh, xung quanh họ hàng cười nhạo chế giễu.',
            voiceover: 'Ba năm làm rể nhà họ Lâm, Diệp Thần cắn răng chịu đựng mọi cay đắng, chỉ để bảo vệ lời thề với người đã khuất. Nhưng hôm nay, giọt nước đã tràn ly!',
            sound: 'Tiếng gốm vỡ sắc lẹm, tiếng cười cợt chói tai, nhạc nền u ám dồn dập.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Thiếu gia ép hôn - Đơn ly hôn đặt lên bàn',
            visual: 'Vương Khắc ung dung bước vào sảnh với bộ vest bảnh bao, ném xấp tiền cọc 50 tỷ cùng tờ đơn ly hôn thẳng vào ngực Diệp Thần.',
            voiceover: 'Vương Khắc: "Ký vào đây rồi cút khỏi nhà họ Lâm! Một tên rác rưởi ăn bám như mày không có tư cách đứng cạnh Thanh Uyển!". Mẹ vợ bên cạnh đon đả cúi chào gã công tử hách dịch, ép Diệp Thần phải điểm chỉ.',
            sound: 'Tiếng nhạc trầm căng thẳng, tiếng cười đắc thắng của Vương Khắc.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Quyết định đoạn tuyệt - Tự tay xé nát hợp đồng',
            visual: 'Diệp Thần ngẩng đầu lên. Ánh mắt anh không còn vẻ cam chịu mà ánh lên tia nhìn sắc bén như lưỡi dao cạo. Anh cầm tờ đơn ly hôn xé toạc làm đôi trước ánh mắt kinh ngạc của cả khán phòng.',
            voiceover: 'Diệp Thần: "Ly hôn? Nhà họ Lâm các người quả thực không xứng với tôi. Từ giây phút này, ân tình ba năm... chính thức chấm dứt!". Tiếng xé giấy vang lên rành rọt như một tiếng sét giữa sảnh tiệc.',
            sound: 'Âm thanh xé giấy vang dội (SFX Ripping paper), nhịp tim đập thình thịch.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Vương Khắc tức giận - Đe dọa trừng phạt',
            visual: 'Vương Khắc đập bàn quát lớn, vẫy tay gọi 10 tên vệ sĩ áo đen lực lưỡng lao vào định bẻ gãy tay Diệp Thần.',
            voiceover: 'Vương Khắc: "Mày muốn chết hả thằng vô dụng? Hôm nay tao sẽ đánh gãy hai chân mày rồi ném ra bờ sông!". Nhưng Diệp Thần chỉ cười khẩy, tay nhẹ nhàng chạm vào chiếc nhẫn Long Đầu trên ngón áp út.',
            sound: 'Tiếng bước chân dồn dập của đám côn đồ, tiếng gầm thét, nhạc cao trào kịch tính.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Trực thăng xuất kích',
            visual: 'Mái kính biệt thự rung chuyển dữ dội. Gió bão quét qua làm bay toàn bộ hoa bàn tiệc. 10 chiếc trực thăng quân sự sơn phù hiệu Long Vương hạ thấp độ cao, bóng đen phủ kín cả dinh thự!',
            voiceover: 'Loa truyền thanh từ trực thăng vang vọng khắp thành phố: "Chiến Thần Điện 10 vạn tướng sĩ... Cung nghênh Long Vương quy vị!". Mặt mẹ vợ và Vương Khắc cắt không còn giọt máu!',
            sound: 'Tiếng cánh quạt trực thăng gầm rú cực mạnh, tiếng còi hú báo động, kết thúc bằng tiếng bass drop chấn động!'
          }
        ]
      },
      {
        episodeNumber: 2,
        title: 'Tập 2: Thần Long Xuất Thế & Vả Mặt Công Tử Tài Phiệt',
        hook: 'Thủ lĩnh vệ binh quỳ rạp trước mặt Diệp Thần, trao chiếc áo choàng Thống Soái nạm vàng ròng!',
        cliffhanger: 'Vương Khắc run rẩy nhận điện thoại từ chủ tịch tập đoàn: "Mày đắc tội với ai? Tập đoàn nhà chúng ta vừa phá sản rồi!"',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Hook tập 2 - Tướng quân quỳ lạy',
            visual: 'Cửa sảnh tiệc bị đá tung, một vị tướng mặc lễ phục quân hàm 5 sao sải bước vào, quỳ một gối trước mặt Diệp Thần.',
            voiceover: 'Toàn bộ gia tộc họ Lâm chết lặng. Người đang quỳ trước tên con rể vô dụng lại chính là Tổng Chỉ Huy Quân Khu phương Bắc mà ngay cả thị trưởng cũng phải kính nể ba phần!',
            sound: 'Tiếng bước chân đanh thép trên sàn đá, tiếng va chạm kim loại của huân chương.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Mẹ vợ ngơ ngác - Nghi ngờ đóng kịch',
            visual: 'Mẹ vợ run rẩy chỉ tay, cố chấp la hét: "Giả vờ! Mày thuê diễn viên quần chúng đúng không? Nó chỉ là thằng rửa bát mồ côi thôi!".',
            voiceover: 'Bà Triệu gào thét mất kiểm soát: "Vương thiếu gia mau gọi cảnh sát bắt nó đi!". Nhưng Vương Khắc lúc này trán vã mồ hôi như tắm, hai chân bắt đầu run rẩy khi nhìn thấy huy hiệu rồng vàng thật trên ngực áo Diệp Thần.',
            sound: 'Tiếng thở gấp, tiếng thì thầm bàn tán xôn xao của khách khứa.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Thẻ đen vạn tỷ xuất hiện',
            visual: 'Diệp Thần rút từ túi áo ra chiếc thẻ đen Black Card mạ vàng Centurion, ném thẳng vào mặt Vương Khắc.',
            voiceover: 'Diệp Thần: "50 tỷ của mày to lắm sao? Trong chiếc thẻ này có 100 tỷ đô la tiền mặt. Tập đoàn Vương Thị của mày, tôi cho 3 phút để biến mất khỏi đất nước này!".',
            sound: 'Tiếng thẻ va vào bàn kim loại "keng", tiếng nhạc dồn dập nghẹt thở.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Lệnh phong sát toàn diện',
            visual: 'Vị tướng bên cạnh lập tức rút bộ đàm: "Ra lệnh cho Cục Tài Chính: Đình chỉ niêm yết cổ phiếu Vương Thị, đóng băng mọi tài khoản ngân hàng!".',
            voiceover: 'Từng câu từng chữ như bản án tử hình giáng xuống đầu gã thiếu gia kiêu ngạo. Vương Khắc bật cười gượng gạo nhưng đôi bàn tay đang bấm điện thoại không ngừng run rẩy.',
            sound: 'Tiếng tút tút điện thoại khẩn cấp, tiếng chuông báo tin nhắn dồn dập.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Cuộc gọi tuyệt mệnh',
            visual: 'Chuông điện thoại của Vương Khắc reo vang. Anh ta bật loa ngoài, đầu dây bên kia tiếng người cha gào thét trong tuyệt vọng.',
            voiceover: 'Cha Vương Khắc: "Nghịch tử! Mày đã chọc giận nhân vật thần thánh nào? Cổ phiếu về 0, chủ nợ siết nhà, gia tộc họ Vương chúng ta... hoàn toàn sụp đổ rồi!". Vương Khắc ngã quỵ xuống sàn!',
            sound: 'Tiếng gào thét điên loạn qua loa điện thoại, tiếng đổ vỡ, nhạc dừng đột ngột tạo sự sốc!'
          }
        ]
      },
      {
        episodeNumber: 3,
        title: 'Tập 3 (Đại Kết Cục): Mẹ Vợ Quỳ Lạy & Ân Đoạn Nghĩa Tuyệt',
        hook: 'Mẹ vợ bò lết dưới sàn ôm chặt ống quần Diệp Thần, khóc lóc van xin tha thứ!',
        cliffhanger: 'Diệp Thần nắm tay Thanh Uyển bước lên chuyên cơ, để lại cả gia tộc chìm trong biển hối hận khôn nguôi!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Sự sụp đổ của kẻ phản diện',
            visual: 'Vương Khắc ôm đầu quỳ sụp, mắt đờ đẫn nhìn Diệp Thần. Mẹ vợ lùi lại vài bước rồi ngã ngồi xuống ghế sô pha, mặt trắng bệch.',
            voiceover: 'Chỉ trong đúng 3 phút, một đế chế tài chính hàng đầu đã bị san phẳng. Toàn bộ người nhà họ Lâm lúc này mới nhận ra, kẻ ăn bám suốt 3 năm qua chính là vị chúa tể mà họ không bao giờ với tới!',
            sound: 'Tiếng thở dốc của Vương Khắc, tiếng khóc thút thít của mẹ vợ.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Màn lật mặt trơ trẽn của mẹ vợ',
            visual: 'Bà Triệu bỗng đổi sắc mặt, chạy tới quỳ gối dưới chân Diệp Thần, đưa hai tay vuốt vạt áo anh.',
            voiceover: 'Bà Triệu khóc lóc: "Diệp Thần... à không, Long Vương gia! Mẹ biết ngay con là phượng hoàng trong loài người mà! Vừa nãy mẹ chỉ thử lòng con thôi, Thanh Uyển yêu con nhất trên đời!".',
            sound: 'Nhạc châm biếm xen lẫn kịch tính, tiếng tát tai bất ngờ vang lên.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Vả mặt đanh thép',
            visual: 'Diệp Thần lạnh lùng rút chân lại, ánh mắt không một gợn sóng nhìn người phụ nữ tham lam trơ trẽn.',
            voiceover: 'Diệp Thần: "Thử lòng? Lúc các người hắt nước bẩn, lúc ép tôi ký đơn ly hôn, sao không nghĩ tới ngày hôm nay? Ba năm qua, bát cơm tôi ăn của nhà họ Lâm, tôi đã trả đủ bằng việc cứu công ty các người ba lần!".',
            sound: 'Tiếng trống trận dồn dập, âm thanh đanh thép đầy uy quyền.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Chỉ giữ lại chân tình',
            visual: 'Diệp Thần quay sang Lâm Thanh Uyển. Ánh mắt anh dịu lại, đưa tay về phía cô gái đang ngấn lệ.',
            voiceover: 'Diệp Thần: "Thanh Uyển, em có nguyện ý đi cùng anh không? Anh sẽ cho em cả giang sơn này!". Thanh Uyển gật đầu trong nước mắt, đặt bàn tay nhỏ bé vào lòng bàn tay ấm áp của anh.',
            sound: 'Nhạc tình cảm du dương ấm áp nhưng vẫn hùng tráng.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Đại kết cục viên mãn - Long Vương thăng hoa',
            visual: 'Hai người sải bước ra thảm đỏ dẫn lên chuyên cơ Long Vương. Phía sau lưng, cả gia tộc họ Lâm đập đầu van xin xin tha thứ nhưng cánh cửa phi cơ đã khép lại.',
            voiceover: 'Cánh cửa đóng lại, động cơ gầm vang. Một hành trình mới bắt đầu: Long Vương tái xuất, thiên hạ khuynh đảo, kẻ bạc nghĩa nhận lấy bài học muôn đời!',
            sound: 'Tiếng phản lực xé gió bay lên bầu trời xanh, nhạc chiến thắng vang dội đầy thỏa mãn!'
          }
        ]
      }
    ]
  },

  // 2. TRÙNG SINH BÁO THÙ (TRỌN BỘ 3 TẬP CHUẨN 2 PHÚT)
  {
    id: 'trung-sinh-bao-thu-thien-kim',
    title: 'Trùng Sinh Báo Thù: Thiên Kim Thật Trở Về',
    originalTitle: '真假千金: 重生复仇 (Chân Giả Thiên Kim: Trọng Sinh Phục Thù)',
    genre: 'Trùng Sinh / Báo Thù',
    genreKey: 'trung_sinh',
    tags: ['Trùng sinh', 'Vả mặt trà xanh', 'Thiên kim thật', 'Hào môn', 'Trọn bộ Full Arc'],
    addictionRating: 4.9,
    viewsEstimate: '1.8 Tỷ lượt xem Douyin',
    hook: 'Kiếp trước bị trà xanh hãm hại lấy tủy rồi ném xuống biển, kiếp này trùng sinh đúng ngày xét nghiệm ADN nhận thân!',
    synopsis: 'Kiếp trước, Ninh Duệ là thiên kim thật của gia tộc giàu có nhất thủ đô nhưng bị cô em nuôi trà xanh vu oan giá họa, cha mẹ ruột ruồng bỏ ép hiến tủy cho con nuôi rồi chết thảm nơi đáy biển lạnh giá. Trùng sinh về năm 20 tuổi đúng buổi tiệc công bố kết quả ADN, Ninh Duệ mang theo bản báo cáo pháp y quốc tế, vạch trần âm mưu tráo đổi con gái và liên minh với vị hôn phu quyền lực nhất Bắc Kinh để tống cổ ả trà xanh vào tù, lấy lại toàn bộ gia sản!',
    characters: [
      { name: 'Ninh Duệ', role: 'Nữ chính', description: 'Trùng sinh mang theo ký ức và tài năng y học thiên tài, quyết tâm đòi lại tất cả.' },
      { name: 'Ninh Tuyết', role: 'Trà xanh phản diện', description: 'Con gái nuôi giả tạo, bề ngoài ngây thơ yếu đuối nhưng lòng dạ độc như rắn rết.' },
      { name: 'Phó Tư Viễn', role: 'Nam chính', description: 'Đại lão đứng đầu giới thương trường Bắc Kinh, lạnh lùng tàn nhẫn nhưng sủng vợ vô điều kiện.' },
      { name: 'Bà Ninh', role: 'Mẹ ruột u mê', description: 'Mù quáng tin tưởng con nuôi, coi thường con gái ruột bị thất lạc ở nông thôn.' },
    ],
    viralHighlights: [
      'Ném bản xét nghiệm ADN thật và đoạn ghi âm tráo đổi con vào máy chiếu hội nghị.',
      'Trà xanh quỳ gối xin tha nhưng bị tát liên tiếp hai bạt tai vang dội.',
      'Phó Tư Viễn tuyên bố huỷ diệt kẻ bắt nạt vợ và tổ chức hôn lễ thế kỷ.',
    ],
    suggestedEpisodes: 3,
    isFullSeries: true,
    episodes: [
      {
        episodeNumber: 1,
        title: 'Tập 1: Giọt Máu Trùng Sinh & Bản Xét Nghiệm Đẫm Máu',
        hook: 'Mở mắt thấy mình đang đứng trước bục tiệc nhận thân, trên tay trà xanh là bản ADN giả!',
        cliffhanger: 'Màn hình máy chiếu bất ngờ nhảy sang đoạn video trà xanh bỏ độc vào ly rượu của mẹ ruột!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Mở màn - Ác mộng kiếp trước',
            visual: 'Hình ảnh nước biển lạnh buốt và tiếng cười the the của Ninh Tuyết. Chớp mắt chuyển sang ánh đèn chùm pha lê rực rỡ của khách sạn 7 sao.',
            voiceover: 'Cảm giác ngạt thở dưới đáy biển sâu vẫn còn bỏng rát nơi cổ họng. Nhưng khi mở mắt ra, Ninh Duệ nhận ra mình đã sống lại! Sống lại đúng ngày gia tộc Ninh Thị họp báo nhận con!',
            sound: 'Tiếng sóng biển gầm rú chuyển đột ngột sang tiếng ly rượu va chạm leng keng và tiếng vĩ cầm.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Trà xanh diễn kịch ngây thơ',
            visual: 'Ninh Tuyết mặc đầm trắng tinh khôi, hai mắt đỏ hoe cầm khăn giấy lau nước mắt, đứng cạnh bà Ninh.',
            voiceover: 'Ninh Tuyết nức nở: "Chị Ninh Duệ từ nông thôn lên chịu nhiều khổ cực, con xin nhường lại danh phận thiên kim cho chị... Dù chị có ghét con, con cũng không oán trách!". Khách khứa nhìn Ninh Duệ với ánh mắt khinh miệt.',
            sound: 'Tiếng xì xào phẫn nộ: "Đúng là đồ vô ơn từ quê lên, vừa về đã bắt nạt em gái!".'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Nụ cười chết chóc của người trùng sinh',
            visual: 'Ninh Duệ không hề tức giận. Cô nhếch mép cười khẽ, rút từ túi xách ra chiếc USB mạ vàng cắm thẳng vào cổng điều khiển màn hình LED.',
            voiceover: 'Ninh Duệ: "Nhường? Một đứa con gái của bảo mẫu năm xưa lén tráo đổi con trẻ, lại dám đứng đây nói từ nhường sao?". Tiếng nói trong trẻo mà uy lực khiến cả khán phòng nín thở.',
            sound: 'Nhạc điện tử dồn dập, tiếng phím bấm máy tính "tách" dứt khoát.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Vạch trần ADN thật',
            visual: 'Màn hình LED khổng lồ 200 inch bật sáng, hiện rõ bản kết luận giám định gen của Viện Giám Định Quốc Tế: Tỷ lệ huyết thống 99.99% giữa Ninh Duệ và chủ tịch Ninh!',
            voiceover: 'Ninh Duệ: "Đây mới là bản ADN được niêm phong từ Viện Pháp Y. Còn tờ giấy trên tay cô... là do viện trưởng tư nhân nhận 2 tỷ đồng làm giả lúc sáng nay!". Mặt Ninh Tuyết cắt không còn giọt máu.',
            sound: 'Tiếng máy ảnh chớp lia lịa "tạch tạch tạch", tiếng phóng viên xôn xao.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Video độc dược',
            visual: 'Chưa dừng lại ở đó, màn hình nhảy sang một đoạn video giám sát trong phòng bếp: Ninh Tuyết đang lén rút ống thuốc lạ rắc vào ly yến của bà Ninh!',
            voiceover: 'Ninh Duệ gằn từng giọng: "Mẹ ruột à, đây chính là đứa con gái ngoan mà mẹ ngày đêm yêu chiều đấy!". Ninh Tuyết hét lên lao tới giật dây điện!',
            sound: 'Tiếng thét kinh hoàng của Ninh Tuyết, tiếng bà Ninh làm rơi ly rượu vỡ tan tành!'
          }
        ]
      },
      {
        episodeNumber: 2,
        title: 'Tập 2: Bạt Tai Trừng Phạt & Đại Lão Xuất Hiện',
        hook: 'Bà Ninh run rẩy vung tay giáng một cái tát trời giáng vào mặt trà xanh!',
        cliffhanger: 'Đại lão quyền lực nhất Bắc Kinh đẩy cửa bước vào: "Vợ chưa cưới của tôi, ai dám động vào một cọng tóc?"',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Cái tát thức tỉnh',
            visual: 'Bà Ninh run rẩy bước tới, bàn tay vung lên tát thẳng vào mặt Ninh Tuyết khiến cô ta ngã dúi dụi xuống sàn.',
            voiceover: 'Một cái tát nảy lửa xé toạc lớp mặt nạ thánh thiện suốt hai mươi năm! Đứa con gái mà bà yêu thương hết mực lại từng ngày chuốc thuốc độc để thâu tóm di chúc gia tộc!',
            sound: 'Tiếng tát cực mạnh (SFX Slap echo), tiếng thở gấp bàng hoàng.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Trà xanh lộ nguyên hình ác quỷ',
            visual: 'Ninh Tuyết bò dậy, khóe môi rỉ máu, ánh mắt trở nên điên loạn gầm gừ.',
            voiceover: 'Ninh Tuyết gào thét: "Đúng thế đấy! Bà già ngu ngốc! Nếu không vì tiền của nhà họ Ninh, ai thèm làm con gái bà? Tao sẽ giết mày, con khốn Ninh Duệ!". Cô ta rút chiếc kéo tỉa hoa trên bàn lao về phía Ninh Duệ.',
            sound: 'Tiếng kim loại rít lên, tiếng phụ nữ la hét hoảng loạn.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Phản đòn chuẩn xác',
            visual: 'Ninh Duệ không hề nao núng. Với kỹ năng chiến đấu kiếp trước, cô bắt lấy cổ tay Ninh Tuyết, vặn ngược khớp khiến chiếc kéo rơi leng keng xuống sàn.',
            voiceover: 'Ninh Duệ lạnh lùng: "Muốn đụng vào tôi? Cô chưa đủ tư cách!". Một cước đá thẳng vào đầu gối khiến Ninh Tuyết phải quỳ sụp xuống đất.',
            sound: 'Tiếng khớp xương rắc nhẹ, tiếng kéo rơi xuống sàn đá hoa cương.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Đám bảo vệ lao vào can thiệp',
            visual: 'Gia đình bạn trai cũ của Ninh Tuyết xông ra đòi bảo vệ người tình, định dùng thế lực chèn ép Ninh Duệ.',
            voiceover: 'Thiếu gia nhà họ Triệu hống hách: "Cho dù cô là thiên kim thật thì sao? Nhà họ Triệu chúng tôi nắm giữ huyết mạch kinh tế của Ninh Thị, hôm nay cô đừng hòng bước ra khỏi cửa lành lặn!".',
            sound: 'Nhạc nền dồn nén căng thẳng cực điểm.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Phó Tư Viễn giáng lâm',
            visual: 'Cánh cửa đôi đại sảnh bật mở tung. Một người đàn ông cao lớn mặc măng-tô đen, khí thế bức người sải bước vào giữa hàng chục vệ sĩ áo vest.',
            voiceover: 'Phó Tư Viễn lạnh lùng lên tiếng: "Nhà họ Triệu các người... muốn cô ấy không ra được cửa sao? Vậy thì ngày mai, gia tộc họ Triệu hãy chuẩn bị xóa tên khỏi bản đồ kinh đô!". Cả hội trường lập tức chết lặng!',
            sound: 'Tiếng giày da nện trên thảm đỏ chậm rãi uy nghiêm, nhạc bass rền vang.'
          }
        ]
      },
      {
        episodeNumber: 3,
        title: 'Tập 3 (Đại Kết Cục): Tống Kẻ Ác Vào Tù & Hôn Lễ Thế Kỷ',
        hook: 'Cảnh sát hình sự ập vào sảnh tiệc còng tay ả trà xanh và gã tình nhân trước ống kính truyền hình trực tiếp!',
        cliffhanger: 'Phó Tư Viễn quỳ một gối trao nhẫn kim cương 100 carat cho Ninh Duệ, tuyên bố trao tặng một nửa tập đoàn cho phu nhân!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Còng số 8 sáng loáng',
            visual: 'Đội cảnh sát kinh tế và hình sự bước vào hội trường, rút thẻ ngành và lệnh bắt giữ khẩn cấp.',
            voiceover: 'Cảnh sát trưởng: "Ninh Tuyết, Triệu Tuấn! Hai người bị bắt vì tội cố ý mưu sát, gian lận thương mại và rửa tiền xuyên quốc gia!". Tiếng còng số 8 khóa chặt cổ tay hai kẻ ác.',
            sound: 'Tiếng còng kim loại "cạch", tiếng phóng viên bấm máy liên hồi.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Lời van xin muộn màng của cha mẹ',
            visual: 'Ông bà Ninh nghẹn ngào bước tới nắm tay con gái ruột Ninh Duệ, nước mắt giàn giụa xin tha thứ vì đã nghi oan bao năm.',
            voiceover: 'Bà Ninh: "Con gái ơi, mẹ sai rồi! Mẹ mù quáng để kẻ ngoài hãm hại con, con hãy tha thứ cho cha mẹ!". Nhưng Ninh Duệ chỉ khẽ rút tay lại, ánh mắt bình thản như nước.',
            sound: 'Nhạc vĩ cầm da diết, tiếng nức nở hối hận.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Tiếp quản tập đoàn gia tộc',
            visual: 'Luật sư trưởng của Phó thị bước lên bục, trình ra bản hợp đồng chuyển nhượng 51% cổ phần kiểm soát toàn bộ Ninh Thị cho Ninh Duệ.',
            voiceover: 'Ninh Duệ đứng trước bục phát biểu: "Từ hôm nay, Ninh thị do tôi toàn quyền tiếp quản. Bất kỳ kẻ nào từng hãm hại tôi, tôi sẽ thanh trừng từng người một!". Cả khán phòng vỗ tay sấm dậy.',
            sound: 'Tiếng vỗ tay rền vang, nhạc chiến thắng ngân nga đầy kiêu hãnh.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Màn cầu hôn chấn động',
            visual: 'Phó Tư Viễn sải bước đến bên cạnh cô, trước mặt hàng triệu khán giả xem truyền hình, anh quỳ xuống một chân mở chiếc hộp nhung đỏ.',
            voiceover: 'Phó Tư Viễn: "Kiếp trước anh đến muộn để em chịu khổ, kiếp này anh xin dùng cả sinh mệnh và đế chế Phó Thị để bảo bọc em trọn đời. Em đồng ý làm vợ anh chứ?".',
            sound: 'Nhạc tình yêu ngọt ngào sâu lắng, tiếng trầm trồ của hàng ngàn người.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Đại kết cục viên mãn - Hạnh phúc trọn vẹn',
            visual: 'Ninh Duệ mỉm cười gật đầu, chiếc nhẫn kim cương lấp lánh được đeo vào ngón tay. Hai người trao nhau cái ôm ấm áp giữa pháo hoa rực sáng.',
            voiceover: 'Mối thù kiếp trước đã trả sạch, kiếp này tái sinh nắm trọn quyền lực và tình yêu đích thực. Một chương mới rực rỡ bắt đầu!',
            sound: 'Tiếng pháo hoa nổ vang trời, giai điệu hào hùng khép lại toàn bộ series đầy thỏa mãn!'
          }
        ]
      }
    ]
  },

  // 3. GIẢ NGHÈO XEM MẮT (TRỌN BỘ 3 TẬP CHUẨN 2 PHÚT)
  {
    id: 'gia-ngheo-xem-mat-nu-tong-tai',
    title: 'Giả Nghèo Đi Xem Mắt Gặp Ngay Nữ Tổng Tài',
    originalTitle: '装穷相亲: 闪婚万亿女总裁 (Trang Cùng Tương Thân: Thiểm Hôn Nữ Tổng Tài)',
    genre: 'Giả Nghèo / Xem Mắt',
    genreKey: 'gia_ngheo',
    tags: ['Giả nghèo', 'Xem mắt', 'Chủ tịch giấu nghề', 'Hài hước', 'Trọn bộ Full Arc'],
    addictionRating: 4.9,
    viewsEstimate: '2.1 Tỷ lượt xem Douyin',
    hook: 'Chủ tịch tập đoàn nghìn tỷ mặc áo shipper đi xem mắt bị gái thực dụng tạt nước, nữ chủ tịch bàn bên kéo tay đi đăng ký kết hôn!',
    synopsis: 'Lục Phong - chủ tịch tập đoàn công nghệ Lục Thị nghìn tỷ, vì chán ngấy những cô nàng đào mỏ nên quyết định mặc đồ shipper đi xem mắt. Bị cô gái thực dụng mắng chửi thậm tệ, nữ tổng tài số 1 thành phố Thẩm Nhược Hy ngồi bàn bên cạnh bỗng đứng dậy kéo anh đi đăng ký kết hôn chớp nhoáng. Qua 3 tập phim kịch tính và hài hước, Lục Phong vừa giả nghèo vừa bảo vệ vợ khỏi gia tộc chèn ép, để rồi trong tiệc mừng công bố thân phận chủ tịch vạn tỷ khiến tất cả kẻ khinh thường phải sụp đổ!',
    characters: [
      { name: 'Lục Phong', role: 'Nam chính', description: 'Chủ tịch tỷ đô ẩn thân làm shipper để tìm kiếm chân ái đời mình.' },
      { name: 'Thẩm Nhược Hy', role: 'Nữ chính', description: 'Nữ chủ tịch xinh đẹp khí chất ngời ngời, đang cần tìm người kết hôn gấp để đối phó gia tộc ép hôn.' },
      { name: 'Vương Mỹ Lệ', role: 'Gái đào mỏ', description: 'Đối tượng xem mắt thực dụng, coi thường người nghèo.' },
      { name: 'Lý thiếu', role: 'Công tử phản diện', description: 'Theo đuổi Thẩm Nhược Hy bất thành, luôn tìm cách bêu rếu Lục Phong.' },
    ],
    viralHighlights: [
      'Gái thực dụng đòi quà 1 tỷ bị bóc phốt hóa đơn thuê túi hiệu.',
      'Nữ tổng tài kéo tay shipper đi đăng ký kết hôn trước mắt cả nhà hàng.',
      'Lục Phong mở cốp xe máy cà tàng ném ra hợp đồng thâu tóm toàn bộ cao ốc của kẻ phản diện.',
    ],
    suggestedEpisodes: 3,
    isFullSeries: true,
    episodes: [
      {
        episodeNumber: 1,
        title: 'Tập 1: Bát Nước Xem Mắt & Hôn Nhân Chớp Nhoáng',
        hook: 'Ả gái thực dụng cầm ly trà đá hắt thẳng vào bộ đồ shipper của Lục Phong!',
        cliffhanger: 'Nữ chủ tịch Thẩm Nhược Hy kéo Lục Phong lên chiếc Maybach: "Tôi cho anh 100 triệu mỗi tháng, chịu cưới tôi không?"',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Hook mở màn - Xem mắt thảm họa',
            visual: 'Trong nhà hàng Michelin sang trọng, cô gái trang điểm đậm chĩa móng tay sơn đỏ vào mặt Lục Phong đang mặc áo khoác shipper giao hàng.',
            voiceover: 'Vương Mỹ Lệ khinh bỉ: "Một tháng lương 8 triệu bạc mà cũng dám mơ trèo cao với tôi? Không có nhà lầu xe hơi thì về quê chăn vịt đi!".',
            sound: 'Tiếng ly cà phê đập mạnh xuống bàn, tiếng nhạc hài châm biếm.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Đòi sính lễ trên trời',
            visual: 'Mỹ Lệ chìa ra tờ giấy danh sách: "Sính lễ 2 tỷ, sang tên sổ đỏ biệt thự cho tôi đứng tên một mình, hàng tháng nộp hết lương cho mẹ tôi quản lý!".',
            voiceover: 'Lục Phong bình thản húp một ngụm nước trà: "Cô gái à, cô đi tìm người kết hôn hay đi tìm cây ATM biết đi vậy?". Mỹ Lệ tức đỏ mặt, giơ ly nước định tạt vào mặt anh.',
            sound: 'Tiếng cười khẩy, tiếng gió rít khi tay cô ta vung lên.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Bàn tay cứu nguy bất ngờ',
            visual: 'Một bàn tay trắng nõn thon thả với chiếc đồng hồ Patek Philippe bạc tỷ bất ngờ giữ chặt cổ tay Vương Mỹ Lệ giữa không trung.',
            voiceover: 'Một giọng nói lạnh lùng mà quyến rũ vang lên: "Nhà hàng công cộng, người phụ nữ vô văn hóa này từ đâu tới đây quấy rối?". Mọi ánh mắt đổ dồn về người phụ nữ vừa bước ra từ bàn VIP.',
            sound: 'Tiếng giày cao gót nhịp nhàng, tiếng trầm trồ của thực khách: "Đó là Thẩm Nhược Hy, nữ hoàng thương trường!".'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Vả mặt cô gái thực dụng',
            visual: 'Thẩm Nhược Hy ném tập hồ sơ lên bàn: "Túi xách Chanel giả mượn của bạn gái, tiền ăn trưa cũng chưa trả. Có tư cách gì chê bai người khác?". Mỹ Lệ xấu hổ cầm túi che mặt chạy thục mạng ra cửa.',
            voiceover: 'Chỉ bằng ba câu nói sắc bén, nữ thần đã tiễn ả đào mỏ về nơi sản xuất. Nhưng bất ngờ nhất là giây phút tiếp theo!',
            sound: 'Tiếng chụp ảnh, tiếng cười chế giễu của mọi người xung quanh.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Lời cầu hôn sét đánh',
            visual: 'Thẩm Nhược Hy quay sang Lục Phong, ghé sát tai anh với ánh mắt nửa cười nửa nghiêm túc, rút ra chiếc thẻ ngân hàng Platinum.',
            voiceover: 'Thẩm Nhược Hy: "Anh đẹp trai, tôi đang bị gia đình ép lấy gã công tử lăng nhăng. Anh có mang chứng minh nhân dân theo không? Cùng tôi đi đăng ký kết hôn, tôi bao nuôi anh cả đời!". Lục Phong sững sờ!',
            sound: 'Nhạc tình yêu pha chút hồi hộp giật gân, tiếng drumroll kết tập cực cuốn!'
          }
        ]
      },
      {
        episodeNumber: 2,
        title: 'Tập 2: Ra Mắt Gia Tộc Hào Môn & Thiếu Gia Khiêu Khích',
        hook: 'Vừa dắt chồng mới cưới về dinh thự, gã thiếu gia phản diện ném chùm chìa khóa Ferrari trúng chân Lục Phong!',
        cliffhanger: 'Lý thiếu gọi điện triệu tập tổng giám đốc tập đoàn Lục Thị đến sa thải Lục Phong, nhưng đầu dây bên kia bảo chủ tịch đang ở ngay trong nhà!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Bước vào dinh thự hào môn',
            visual: 'Lục Phong vẫn mặc áo thun giản dị đi cạnh Thẩm Nhược Hy bước vào biệt phủ nhà họ Thẩm. Họ hàng nhìn anh với ánh mắt dè bỉu.',
            voiceover: 'Vừa ký giấy kết hôn xong, Thẩm Nhược Hy dẫn ngay người chồng shipper về ra mắt để dập tắt âm mưu ép gả của gia đình. Một trận sóng gió lập tức bùng nổ!',
            sound: 'Tiếng bước chân trong sảnh đá cẩm thạch, tiếng cười khẩy của họ hàng.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Lý thiếu gia ra oai',
            visual: 'Lý thiếu ngậm điếu xì gà, xoay chiếc chìa khóa Ferrari trên tay: "Nhược Hy, em vì trốn tránh anh mà nhặt một tên ăn mày ngoài đường về làm chồng sao?".',
            voiceover: 'Hắn ném tờ séc 10 tỷ xuống chân Lục Phong: "Cầm lấy số tiền này mà mua xe mới, rồi cút khỏi tầm mắt của bổn thiếu gia ngay lập tức!".',
            sound: 'Tiếng cười ha hả trịch thượng của đám công tử con nhà giàu.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Sự bảo vệ của nữ tổng tài',
            visual: 'Thẩm Nhược Hy lập tức bước lên chắn trước mặt Lục Phong, gạt phăng tờ séc của Lý thiếu.',
            voiceover: 'Thẩm Nhược Hy: "Lý thiếu gia, anh ấy là chồng hợp pháp của tôi! Dù anh ấy có nghèo, cũng hơn vạn lần loại người đạo đức giả như anh!". Lục Phong nhìn bóng lưng vợ, trong lòng dâng lên cảm giác ấm áp.',
            sound: 'Tiếng giấy rơi lả tả, ánh mắt Lục Phong trở nên thâm trầm.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Lý thiếu dùng quyền lực bức bách',
            visual: 'Lý thiếu tức giận rút điện thoại: "Một thằng shipper quèn dám đọ với tao? Tập đoàn giao hàng của mày do tập đoàn Lục Thị nắm quyền kiểm soát! Tao gọi một cuộc điện thoại là mày mất việc ra đường ăn xin!".',
            voiceover: 'Hắn bấm loa ngoài gọi cho Tổng Giám Đốc chi nhánh Lục Thị, kiêu ngạo ra lệnh sa thải nhân viên tên Lục Phong ngay lập tức.',
            sound: 'Tiếng chuông điện thoại tút tút, nhạc nền dồn dập.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Cuộc gọi phản tác dụng',
            visual: 'Loa ngoài vang lên giọng nói run rẩy của Tổng Giám Đốc chi nhánh.',
            voiceover: 'Tổng Giám Đốc: "Lý thiếu gia điên rồi sao? Lục Phong chính là Chủ Tịch Sáng Lập kiêm Cổ Đông Lớn Nhất của tập đoàn Lục Thị! Ngài ấy đang ở cạnh anh sao?". Lý thiếu đứng hình như tượng đá!',
            sound: 'Tiếng thở dốc nghẹn ngào từ điện thoại, tiếng bass drop rung chuyển cả sảnh tiệc!'
          }
        ]
      },
      {
        episodeNumber: 3,
        title: 'Tập 3 (Đại Kết Cục): Thân Phận Chủ Tịch Vạn Tỷ & Đám Cưới Thế Kỷ',
        hook: 'Đoàn xe 20 chiếc Rolls-Royce cùng dàn lãnh đạo tập đoàn Lục Thị bước vào sảnh cung kính cúi đầu chào Chủ Tịch!',
        cliffhanger: 'Thẩm Nhược Hy đỏ mặt khi phát hiện người chồng shipper cô bao nuôi thực chất sở hữu khối tài sản gấp 10 lần gia tộc cô!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Sự thật hé lộ',
            visual: 'Cửa biệt phủ mở toang, 20 vệ sĩ vest đen cùng các thành viên Hội Đồng Quản Trị tập đoàn công nghệ Lục Thị sải bước vào, đồng loạt cúi gập người 90 độ trước mặt Lục Phong.',
            voiceover: 'Toàn thể ban giám đốc đồng thanh: "Kính chào Lục Chủ Tịch! Mọi thủ tục thâu tóm toàn bộ chuỗi bất động sản của nhà họ Lý đã hoàn tất!".',
            sound: 'Tiếng bước chân đồng bộ rầm rập, tiếng hô vang dội làm rung cả trần nhà.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Sự sụp đổ của Lý thiếu gia',
            visual: 'Lý thiếu trượt chân ngã ngồi xuống sàn, mắt nhìn Lục Phong như nhìn thấy quái vật. Chiếc chìa khóa Ferrari rơi khỏi tay lăn lóc.',
            voiceover: 'Lý thiếu run rẩy bò tới: "Lục... Lục tổng! Tôi có mắt như mù không nhận ra Thái Sơn, xin ngài giơ cao đánh khẽ tha cho tập đoàn nhà tôi!". Lục Phong chỉ lạnh lùng bước qua.',
            sound: 'Tiếng khóc lóc van xin thảm thiết, tiếng cười nhạo của khách mời.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Vả mặt gia tộc họ Thẩm',
            visual: 'Những người cô bác họ hàng từng dè bỉu Lục Phong lúc này xúm lại đon đả rót trà, cúi đầu tạ tội xin được tha thứ.',
            voiceover: 'Lục Phong mỉm cười nắm tay Thẩm Nhược Hy: "Nhà họ Thẩm các người chê tôi nghèo, vậy thì số cổ phần 500 tỷ này, tôi xin tặng lại riêng cho vợ tôi là Nhược Hy đứng tên!".',
            sound: 'Tiếng trầm trồ kinh ngạc tột độ của toàn thể quan khách.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Lời tỏ tình chân thật',
            visual: 'Lục Phong nhìn sâu vào đôi mắt long lanh của Thẩm Nhược Hy, rút từ túi áo ra chiếc nhẫn kim cương hồng quý hiếm nhất thế giới.',
            voiceover: 'Lục Phong: "Em từng nói sẽ bao nuôi anh cả đời, hôm nay anh xin đổi lại: để anh bảo vệ và cưng chiều em suốt cả cuộc đời này!". Nhược Hy xúc động ôm chầm lấy anh.',
            sound: 'Nhạc tình yêu du dương ngọt ngào, tiếng vỗ tay rộn rã.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Đại kết cục viên mãn',
            visual: 'Khung cảnh chuyển sang hôn lễ thế kỷ hoành tráng bên bờ biển, hai người trao nhau nụ hôn ngọt ngào dưới ánh hoàng hôn tuyệt đẹp.',
            voiceover: 'Từ một buổi xem mắt giả nghèo đến hôn nhân thế kỷ vạn người ngưỡng mộ. Chân tình đổi lấy chân tâm, hạnh phúc trọn vẹn mãi mãi về sau!',
            sound: 'Tiếng chuông nhà thờ ngân vang, tiếng hò reo chúc phúc kết thúc series cực kỳ viên mãn!'
          }
        ]
      }
    ]
  },

  // 4. MẸ KẾ ĐỘC ÁC VÀ MANH BẢO TRIỆU ĐÔ (TRỌN BỘ 3 TẬP CHUẨN 2 PHÚT)
  {
    id: 'me-ke-manh-bao-trieu-do',
    title: 'Mẹ Kế Độc Ác và Hài Nhi Thiên Tài Triệu Đô',
    originalTitle: '后妈恶毒: 天才萌宝找爹地 (Hậu Ma Ác Độc: Thiên Tài Manh Bảo)',
    genre: 'Mẹ Kế / Manh Bảo',
    genreKey: 'manh_bao',
    tags: ['Manh bảo thiên tài', 'Mẹ kế độc ác', 'Tổng tài tìm vợ', 'Hài hước', 'Trọn bộ Full Arc'],
    addictionRating: 5.0,
    viewsEstimate: '2.5 Tỷ lượt xem Douyin',
    hook: 'Hai nhóc tì 5 tuổi hack toàn bộ màn hình LED tòa nhà chọc trời để bóc phốt mẹ kế độc ác và tìm bố đền mẹ!',
    synopsis: '5 năm trước cô bị mẹ kế và em gái hãm hại mang thai đôi rồi bị đuổi ra nước ngoài trong tủi nhục. 5 năm sau dắt 2 bảo bối thiên tài về nước: bé anh là hacker đứng đầu tổ chức ngầm quốc tế, bé em là thần đồng đầu tư chứng khoán triệu đô. Hai nhóc lập tức vạch trần tội ác của ả mẹ kế trà xanh, trừng phạt kẻ xấu và ép Cố tổng phải dốc hết tài sản quỳ xin mẹ tái hôn!',
    characters: [
      { name: 'Kỷ Thư Nghi', role: 'Nữ chính', description: 'Mẹ đơn thân kiên cường, nhà thiết kế thời trang quốc tế ẩn danh.' },
      { name: 'Cố Thừa Trạch', role: 'Tổng tài', description: 'Chủ tịch tập đoàn Cố Thị, tìm kiếm tung tích mẹ của con mình suốt 5 năm.' },
      { name: 'Bảo Bảo & Bối Bối', role: 'Manh bảo thiên tài', description: 'Cặp song sinh IQ 180, chuyên gia vả mặt kẻ xấu bảo vệ mẹ.' },
      { name: 'Bạch Linh', role: 'Mẹ kế trà xanh', description: 'Mạo nhận ân nhân cứu mạng để chiếm đoạt vị trí Cố phu nhân.' },
    ],
    viralHighlights: [
      'Hai bé con 5 tuổi mặc vest đen mang kính râm chỉ huy hack hệ thống đám cưới.',
      'Mẹ kế đang thử váy cưới thì video ngoại tình bị chiếu lên màn hình sảnh tiệc.',
      'Cố tổng quỳ một gối trước mặt Kỷ Thư Nghi trước sự chứng kiến của 2 con.',
    ],
    suggestedEpisodes: 3,
    isFullSeries: true,
    episodes: [
      {
        episodeNumber: 1,
        title: 'Tập 1: Màn Hack Chấn Động & Lễ Cưới Bị Phá Tan',
        hook: 'Màn hình LED lớn nhất thành phố bất ngờ nhảy sang hình ảnh hai bé con 5 tuổi cực đáng yêu nhưng lời nói cực ngầu!',
        cliffhanger: 'Cố tổng giật phăng hoa cài áo của cô dâu, mắt nhìn chằm chằm vào chiếc lắc tay bạc của Kỷ Thư Nghi!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Hook - Màn hình LED toàn thành phố đổi màu',
            visual: 'Màn hình quảng cáo 3D tại quảng trường trung tâm bỗng chớp giật rồi hiện lên gương mặt hai nhóc tì song sinh cực kháu khỉnh đeo kính râm đen ngầu.',
            voiceover: 'Bé Bảo Bảo: "Alo alo Cố tổng! Bố đang chuẩn bị cưới người đàn bà bắt nạt mẹ con tôi đúng không? Xin lỗi bố nhé, lễ cưới này hủy rồi!".',
            sound: 'Tiếng còi cảnh báo an ninh mạng "bíp bíp", tiếng xôn xao của hàng ngàn người đi đường.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Lễ cưới triệu đô biến thành trò cười',
            visual: 'Bên trong sảnh cưới khách sạn 6 sao, Bạch Linh đang cười rạng rỡ khoác tay Cố tổng thì toàn bộ âm thanh lễ đường chuyển sang giọng trẻ con rành rọt.',
            voiceover: 'Màn hình lớn sân khấu tiệc cưới không chiếu ảnh tân lang tân nương mà chiếu đoạn ghi âm Bạch Linh thuê giang hồ hãm hại Kỷ Thư Nghi 5 năm trước. Khách khứa ồ lên kinh hãi!',
            sound: 'Tiếng xì xào bàn tán: "Trời ơi, hóa ra cô dâu là kẻ độc ác như vậy sao?".'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Cố Thừa Trạch nổi giận lôi đình',
            visual: 'Cố tổng ánh mắt sắc như dao hất văng tay Bạch Linh ra, giật chiếc khăn voan cưới ném xuống sàn đá.',
            voiceover: 'Cố Thừa Trạch: "Bạch Linh! Năm xưa người cứu tôi trong vụ hỏa hoạn thực sự là ai?". Bạch Linh quỳ sụp xuống van lơn nhưng hắn không buồn liếc nhìn một lần.',
            sound: 'Tiếng bước chân gấp gáp, tiếng micro rè rè vì bị ngắt nguồn.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Kỷ Thư Nghi xuất hiện tại sảnh',
            visual: 'Kỷ Thư Nghi mặc bộ đầm dạ hội đỏ quyến rũ dắt theo hai bé con vừa bước vào cửa sảnh tiệc. Khí chất nữ vương ngút ngàn đè bẹp tất cả.',
            voiceover: 'Kỷ Thư Nghi mỉm cười nhẹ: "Cố tổng, Bạch tiểu thư, nghe nói hôm nay là ngày vui, tôi đưa hai con đến chúc mừng một tiếng!". Hai nhóc tì ngoan ngoãn khoanh tay chào bố.',
            sound: 'Nhạc nền biến chuyển sang giai điệu quyến rũ, quyền lực.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Kỷ vật nhận diện',
            visual: 'Gió nhẹ thổi lay cổ tay Kỷ Thư Nghi, để lộ chiếc lắc bạc khắc hình hoa sen duy nhất trên thế giới. Cố tổng trợn tròn mắt lao tới nắm chặt lấy tay cô!',
            voiceover: 'Cố Thừa Trạch run rẩy thốt lên: "Chiếc lắc này... Em mới chính là cô gái đêm đó cứu anh? Em... chính là mẹ của hai đứa bé?". Kết tập trong sự ngỡ ngàng tột độ!',
            sound: 'Tiếng thở dốc nghẹn ngào, nhạc cao trào dồn dập khiến người xem sốt ruột chờ tập sau!'
          }
        ]
      },
      {
        episodeNumber: 2,
        title: 'Tập 2: Hai Manh Bảo Vả Mặt Mẹ Kế & Sự Thật 5 Năm',
        hook: 'Ả mẹ kế lao vào định cào rách mặt Kỷ Thư Nghi thì bị hai nhóc tì bắn súng nước cay xè mắt!',
        cliffhanger: 'Cố tổng ra lệnh phong tỏa toàn bộ tài sản của gia tộc họ Bạch, tống cổ ả mẹ kế ra khỏi biệt thự!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Ả mẹ kế điên cuồng phản đòn',
            visual: 'Bạch Linh gào thét lao tới định tấn công Kỷ Thư Nghi. Hai bé con nhanh như chớp rút từ balo ra khẩu súng bắn bột màu và nước ớt bắn thẳng vào mặt ả.',
            voiceover: 'Bé Bối Bối hét lớn: "Dám bắt nạt mẹ tôi hả đồ đàn bà độc ác!". Bạch Linh ôm mặt la hét đau đớn, lớp phấn trang điểm trôi tuột lộ vẻ nhếch nhác.',
            sound: 'Tiếng xịt nước "phụt phụt", tiếng gào rú thất thanh của Bạch Linh.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Bằng chứng ADN đanh thép',
            visual: 'Bé Bảo Bảo mở máy tính bảng cầm tay, chiếu trực tiếp kết quả xét nghiệm ADN của Cố tổng và hai bé lên màn hình lớn.',
            voiceover: 'Bảo Bảo: "Tỷ lệ trùng khớp gen 99.9%. Bố Cố, năm xưa bà ta lừa bố nói con của bố đã chết, nhưng thực ra mẹ con tôi bị bà ta thuê người bắt cóc vứt ra biển!".',
            sound: 'Tiếng máy ảnh chớp nhoáng, khách khứa la ó phẫn nộ với tội ác tày trời.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Cố Thừa Trạch trừng phạt thích đáng',
            visual: 'Cố tổng siết chặt nắm đấm, ánh mắt bốc hỏa nhìn ả mẹ kế đang quỳ lết dưới sàn.',
            voiceover: 'Cố Thừa Trạch gầm lên: "Bảo vệ đâu! Lôi người phụ nữ này giao cho viện kiểm sát. Tất cả các hợp đồng làm ăn với nhà họ Bạch, lập tức hủy bỏ toàn bộ!".',
            sound: 'Tiếng vệ sĩ kéo lê kẻ ác trên sàn đá, tiếng khóc than xin tha bị dập tắt.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Mẹ con kiên quyết rời đi',
            visual: 'Kỷ Thư Nghi nắm tay hai con xoay người rời đi: "Con tôi, tôi tự nuôi. Cố tổng, chúng ta không có quan hệ gì!".',
            voiceover: 'Năm năm chịu đựng tủi nhục không thể xóa nhòa trong một sớm một chiều. Dù Cố tổng tha thiết gọi với theo, cô vẫn kiên quyết sải bước ra cửa.',
            sound: 'Tiếng giày cao gót dứt khoát, tiếng bước chân vội vã đuổi theo của Cố tổng.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Màn truy thê nghẹt thở',
            visual: 'Cố tổng chạy ra sảnh, chặn ngay trước mũi xe taxi của ba mẹ con, hai đầu gối khuỵu xuống giữa trời mưa!',
            voiceover: 'Cố Thừa Trạch nghẹn ngào: "Thư Nghi, nếu em không tha thứ cho anh, hôm nay anh sẽ quỳ ở đây đến khi em chịu quay về!". Hai nhóc tì nhìn nhau che miệng cười!',
            sound: 'Tiếng phanh xe gấp "két", tiếng sấm rền vang kết thúc tập đầy gay cấn!'
          }
        ]
      },
      {
        episodeNumber: 3,
        title: 'Tập 3 (Đại Kết Cục): Cố Tổng Quỳ Cầu Hôn & Gia Đình Đoàn Tụ',
        hook: 'Hai nhóc tì ra điều kiện: "Bố muốn rước mẹ về thì phải nộp hết sổ đỏ và thẻ ngân hàng cho mẹ giữ!"',
        cliffhanger: 'Đại gia đình 4 người hạnh phúc bước vào lễ đường thế kỷ, kẻ ác nhận án tù 20 năm!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Điều kiện của hai manh bảo',
            visual: 'Hai bé con đứng trước mặt Cố tổng đang ướt sũng dưới mưa, đưa ra bản cam kết dài 10 trang.',
            voiceover: 'Bảo Bảo dõng dạc: "Điều 1: Bố không được để mẹ khóc. Điều 2: Toàn bộ cổ phần Cố Thị sang tên mẹ. Bố làm được thì mẹ con tôi mới cân nhắc!".',
            sound: 'Tiếng mưa rơi nhẹ dần, tiếng lật giấy rột roạt.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Tổng tài không ngần ngại ký tên',
            visual: 'Cố Thừa Trạch mỉm cười không hề do dự, cầm bút ký ngay vào góc hợp đồng rồi giao toàn bộ con dấu tập đoàn cho Kỷ Thư Nghi.',
            voiceover: 'Cố tổng: "Đừng nói là gia sản, tính mạng của anh từ 5 năm trước đã thuộc về ba mẹ con em rồi!". Kỷ Thư Nghi rơi nước mắt, mở chiếc dù che lên đầu anh.',
            sound: 'Nhạc tình yêu ấm áp vang lên xua tan bầu không khí u ám.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Bản án thích đáng cho kẻ ác',
            visual: 'Bản tin truyền hình trực tiếp phát hình ảnh Bạch Linh mặc áo phạm nhân, cúi đầu nhận bản án 20 năm tù vì tội ác năm xưa.',
            voiceover: 'Công lý đã được thực thi. Kẻ gieo rắc đau khổ cuối cùng phải trả giá trong song sắt, vĩnh viễn không còn cơ hội ngóc đầu lên!',
            sound: 'Tiếng búa đập của thẩm phán vang lên đanh thép.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Lễ cưới bù đắp cổ tích',
            visual: 'Khung cảnh chuyển sang lễ đường nguy nga rực rỡ hoa hồng trắng. Hai bé con làm phù dâu phù rể nhí cực kỳ đáng yêu dắt tay mẹ bước vào.',
            voiceover: 'Cố Thừa Trạch đón lấy bàn tay nhỏ bé của người phụ nữ anh yêu nhất, trao nhau nụ hôn minh chứng cho tình yêu vượt qua muôn ngàn trắc trở.',
            sound: 'Tiếng đàn vĩ cầm ngọt ngào, tiếng vỗ tay rộn ràng của toàn thể người thân bạn bè.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Đại kết cục viên mãn',
            visual: 'Bức ảnh chụp gia đình 4 người hạnh phúc rạng ngời được đóng khung mạ vàng, hai nhóc tì cười toe toét bên cha mẹ.',
            voiceover: 'Trải qua bão giông mới thấu hiểu chân tình. Từ nay về sau, chỉ còn yêu thương, bình yên và tiếng cười rộn rã bên mái ấm trọn vẹn!',
            sound: 'Giai điệu ca khúc kết phim ấm áp du dương, khép lại trọn bộ series đầy mỹ mãn!'
          }
        ]
      }
    ]
  },

  // 5. LY HÔN RỒI VỢ CŨ LÀ ĐẠI LÃO (TRỌN BỘ 3 TẬP CHUẨN 2 PHÚT)
  {
    id: 'vo-cu-tro-ve-tong-tai-hoi-han',
    title: 'Ly Hôn Rồi, Tổng Tài Mới Biết Vợ Cũ Là Đại Lão',
    originalTitle: '离婚后前夫追妻火葬场 (Ly Hôn Hậu Tiền Phu Truy Thê Hỏa Táng Tràng)',
    genre: 'Vợ Cũ Đại Lão / Ngược',
    genreKey: 'nguoc_luyen',
    tags: ['Vợ cũ trở về', 'Hối hận', 'Truy thê', 'Đại lão ẩn danh', 'Trọn bộ Full Arc'],
    addictionRating: 4.8,
    viewsEstimate: '1.9 Tỷ lượt xem Douyin',
    hook: 'Vừa ký đơn ly hôn dứt áo ra đi, ngày hôm sau cô xuất hiện trên thảm đỏ với tư cách chủ tịch tập đoàn đối thủ!',
    synopsis: 'Hạ Vy vì yêu Mộ Kính Thần mà nhẫn nhịn làm người vợ hiền thục suốt 3 năm, chịu đựng sự lạnh nhạt và lời sỉ nhục từ gia đình chồng. Khi bạch nguyệt quang của anh ta trở về, cô dứt khoát ký đơn ly hôn không lấy một xu. Ngày hôm sau, cô lột xác thành chủ tịch Rose - người đứng đầu đế chế tài chính lớn nhất châu lục, vả mặt trà xanh tại sàn đấu giá, khiến người chồng cũ phải quỳ dưới mưa hối hận khôn nguôi!',
    characters: [
      { name: 'Hạ Vy', role: 'Nữ chính', description: 'Đại lão tài chính và nhà thiết kế ẩn danh, sau ly hôn hoàn toàn bùng nổ nhan sắc và quyền lực.' },
      { name: 'Mộ Kính Thần', role: 'Tổng tài hối hận', description: 'Tổng tài mù quáng sau khi mất vợ mới nhận ra ai là người quan trọng nhất cuộc đời mình.' },
      { name: 'Bạch Nguyệt', role: 'Trà xanh tiểu thư', description: 'Bề ngoài yếu đuối, luôn dựng chuyện để hãm hại Hạ Vy.' },
    ],
    viralHighlights: [
      'Cảnh xé séc 10 triệu tệ ném vào mặt chồng cũ: "Giữ lấy mà mua thuốc bổ mắt!".',
      'Tổng tài dầm mưa 3 ngày 3 đêm trước cổng biệt thự cầu xin vợ cũ quay về.',
      'Vả mặt bạch nguyệt quang tại sàn đấu giá trang sức quốc tế.',
    ],
    suggestedEpisodes: 3,
    isFullSeries: true,
    episodes: [
      {
        episodeNumber: 1,
        title: 'Tập 1: Ký Đơn Ra Đi & Màn Lột Xác Triệu Đô',
        hook: 'Hạ Vy quăng chiếc nhẫn cưới vào cốc rượu vang của Mộ Kính Thần rồi quay lưng bước đi!',
        cliffhanger: 'Tại buổi tiệc thượng lưu tối hôm sau, người phụ nữ quyền lực bước xuống từ Rolls-Royce Phantom chính là cô!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Ký đơn ly hôn dứt khoát',
            visual: 'Chiếc bút máy mực đen lướt nhanh trên trang giấy ly hôn. Hạ Vy tháo chiếc nhẫn kim cương ném thẳng vào ly rượu của chồng.',
            voiceover: 'Ba năm cống hiến tuổi xuân cho một kẻ vô tâm, hôm nay Hạ Vy chính thức giải thoát cho chính mình. "Mộ Kính Thần, từ nay chúng ta không ai nợ ai!".',
            sound: 'Tiếng nhẫn kim loại rơi vào ly rượu "keng", tiếng cửa kính đóng sập.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Sự ngạo mạn của Mộ tổng',
            visual: 'Mộ Kính Thần châm điếu xì gà, khóe môi khinh bỉ nhìn theo bóng lưng vợ: "Để tôi xem rời khỏi nhà họ Mộ, không có tiền không có quan hệ, cô sống được mấy ngày!".',
            voiceover: 'Bạch nguyệt quang bên cạnh nũng nịu chúc mừng: "Kính Thần, cuối cùng anh cũng bỏ được cô ta rồi!". Nhưng hắn không ngờ, chuỗi ngày hối hận đẫm máu của hắn đã bắt đầu!',
            sound: 'Tiếng bật lửa zippo, tiếng cười đắc thắng ghê tởm của ả nhân tình.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Lột xác trở lại thân phận đại lão',
            visual: 'Hạ Vy bước lên xe sang chuyên dụng, cởi bỏ bộ đồ mặc nhà giản dị, thay bằng bộ váy dạ hội Haute Couture đính kim cương lấp lánh, tháo kính cận khoe trọn nhan sắc tuyệt mỹ.',
            voiceover: 'Hạ Vy cầm điện thoại gọi cho trợ lý: "Thông báo cho các giám đốc khu vực: Chủ tịch Rose chính thức trở lại tiếp quản tập đoàn!".',
            sound: 'Nhạc điện tử dồn dập, tiếng phanh xe thể thao cực gắt.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Hội nghị thượng đỉnh 50 tỷ đô',
            visual: 'Mộ Kính Thần đang đứng trước cửa hội trường VIP để chờ gặp đối tác bí ẩn từ châu Âu nhằm cứu vãn hợp đồng sinh tử của tập đoàn.',
            voiceover: 'Mộ tổng thấp thỏm lo âu: "Chỉ cần chủ tịch Rose chịu gật đầu đầu tư, Mộ Thị sẽ vượt qua khủng hoảng!". Đúng lúc này, đoàn xe hộ tống 12 chiếc Rolls-Royce dừng lại.',
            sound: 'Tiếng động cơ gầm vang, ánh đèn flash chớp sáng rực rỡ.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Sự bàng hoàng của kẻ phản bội',
            visual: 'Vệ sĩ cung kính mở cửa xe, một đôi chân dài thon gọn bước xuống. Hạ Vy tháo kính râm, mỉm cười sắc lạnh nhìn thẳng vào mắt Mộ Kính Thần!',
            voiceover: 'Mộ Kính Thần rớt ly rượu trên tay, lẩm bẩm trong kinh hãi: "Hạ... Hạ Vy? Em... em là chủ tịch Rose sao?!". Cô lướt qua hắn như một cơn gió: "Mộ tổng, hợp đồng của anh... bị hủy rồi!".',
            sound: 'Tiếng ly pha lê vỡ toang dưới sàn, tiếng nhạc bùng nổ cảm xúc tột đỉnh!'
          }
        ]
      },
      {
        episodeNumber: 2,
        title: 'Tập 2: Sàn Đấu Giá Kim Cương & Vả Mặt Trà Xanh',
        hook: 'Bạch Nguyệt vênh váo đòi tranh mua sợi dây chuyền Trái Tim Đại Dương, Hạ Vy giơ biển nâng giá 100 triệu USD trong chớp mắt!',
        cliffhanger: 'Mộ Kính Thần phát hiện nhật ký năm xưa: người cứu anh trong tai nạn tuyết lở là Hạ Vy chứ không phải Bạch Nguyệt!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Đụng độ tại sàn đấu giá quốc tế',
            visual: 'Tại sàn đấu giá trang sức xa hoa nhất thành phố, Bạch Nguyệt khoác tay Mộ Kính Thần bước vào, nhìn thấy Hạ Vy đang ngồi tại hàng ghế danh dự VIP 1.',
            voiceover: 'Bạch Nguyệt mỉa mai: "Một kẻ bị ruồng bỏ như cô lấy tiền đâu vào đây? Chắc là bám víu lão đại gia nào rồi!".',
            sound: 'Tiếng búa gõ khai mạc phiên đấu giá "cộp cộp".'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Cuộc chiến kim tiền nghẹt thở',
            visual: 'Vật phẩm đắt giá nhất - sợi dây chuyền Trái Tim Đại Dương trị giá 20 triệu USD được mang ra. Mộ Kính Thần giơ biển trả giá 30 triệu USD để làm hài lòng người tình.',
            voiceover: 'Hạ Vy bình thản giơ biển: "100 triệu USD tiền mặt!". Cả khán phòng đồng loạt đứng bật dậy kinh hoàng trước mức giá kỷ lục.',
            sound: 'Tiếng trầm trồ ồ lên của hàng trăm tỷ phú, tiếng tim đập thình thịch.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Trà xanh muối mặt',
            visual: 'Mộ Kính Thần không thể theo giá nổi, đành cắn răng buông biển. Người bán đấu giá gõ búa tuyên bố vật phẩm thuộc về chủ tịch Rose.',
            voiceover: 'Hạ Vy bước lên sân khấu nhận dây chuyền, quay sang mỉm cười sắc sảo: "Mộ tổng, tiền tài và đẳng cấp... không phải thứ mà kẻ phản bội như anh có thể đụng tới!".',
            sound: 'Tiếng vỗ tay rầm rộ, mặt Bạch Nguyệt tím tái xấu hổ.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Sự thật tai nạn năm xưa',
            visual: 'Trợ lý riêng của Mộ Kính Thần chạy vào hớt hải đưa tập hồ sơ y tế năm xưa bị giấu kín.',
            voiceover: 'Trợ lý: "Mộ tổng! Người năm xưa bò trên tuyết cõng anh suốt 10 cây số tới bệnh viện chính là phu nhân Hạ Vy! Bạch Nguyệt chỉ là kẻ mạo danh cướp công!".',
            sound: 'Nhạc nền rung chuyển bi tráng, mắt Mộ Kính Thần long sòng sọc.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Sự ân hận tột cùng',
            visual: 'Mộ Kính Thần quay sang bóp chặt cổ tay Bạch Nguyệt, hất ngã cô ta xuống sàn rồi chạy thục mạng ra mưa đuổi theo chiếc xe của Hạ Vy.',
            voiceover: 'Mộ Kính Thần khóc gào trong mưa: "Hạ Vy! Anh sai rồi... Em mới là người anh tìm kiếm cả đời!". Nhưng chiếc siêu xe đã lao vụt đi trong màn đêm lạnh giá!',
            sound: 'Tiếng sấm sét rạch ngang trời, tiếng gào khóc tuyệt vọng trong mưa kết thúc tập!'
          }
        ]
      },
      {
        episodeNumber: 3,
        title: 'Tập 3 (Đại Kết Cục): Dầm Mưa Quỳ Gối & Nữ Vương Độc Lập',
        hook: 'Mộ tổng quỳ gối 3 ngày 3 đêm trước cổng biệt thự của Hạ Vy, kiệt sức đổ gục dưới cơn mưa tầm tã!',
        cliffhanger: 'Hạ Vy ký lệnh mua lại toàn bộ cổ phần Mộ Thị, chính thức trở thành nữ vương tối cao của giới tài chính!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Mộ tổng quỳ dưới mưa xin tha thứ',
            visual: 'Trời mưa như trút nước. Mộ Kính Thần quỳ gối trên nền đường nhựa lạnh buốt trước cổng dinh thự, hai mắt đỏ hoe ngước nhìn lên cửa sổ phòng Hạ Vy.',
            voiceover: 'Ba ngày ba đêm không ăn không uống, vị tổng tài kiêu ngạo ngày nào giờ chỉ còn là một kẻ thảm hại van xin một ánh nhìn từ người vợ cũ.',
            sound: 'Tiếng mưa rơi rả rích, tiếng thở dốc kiệt quệ.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Chiếc ô đen che mưa',
            visual: 'Cánh cổng biệt thự mở ra, Hạ Vy mặc áo măng-tô đen, tay cầm chiếc ô tiến lại gần người đàn ông đang quỳ.',
            voiceover: 'Mộ Kính Thần ngước lên mừng rỡ ôm lấy chân cô: "Vy Vy... Em chịu tha thứ cho anh rồi đúng không? Anh sẽ trả lại tất cả cho em!".',
            sound: 'Tiếng nước bắn tung tóe, tiếng nghẹn ngào van lơn.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Đoạn tuyệt dứt khoát',
            visual: 'Hạ Vy buông chiếc ô xuống che cho anh, nhưng ánh mắt cô hoàn toàn lạnh lùng, không còn một tia vướng bận tình cảm.',
            voiceover: 'Hạ Vy: "Mộ Kính Thần, tôi đến không phải để tha thứ, mà để nói cho anh biết: Gương vỡ thì không lành lại được. Sự nhẫn nhịn năm xưa tôi dành cho anh đã chết rồi!".',
            sound: 'Nhạc dương cầm buồn bã nhưng dứt khoát, tiếng mưa đập vào tán dù.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Thâu tóm toàn diện đế chế',
            visual: 'Cô rút ra bản thông báo tiếp quản tập đoàn: "Công ty nhà anh đã bị quỹ đầu tư của tôi mua lại toàn bộ. Từ nay, anh chỉ là một nhân viên làm thuê!".',
            voiceover: 'Từng câu chữ như nhát dao chấm dứt mọi ảo tưởng của gã đàn ông phản bội. Mộ Kính Thần gục mặt xuống vũng nước trong cay đắng tột cùng.',
            sound: 'Tiếng chuông điện thoại báo tin ngân hàng phong tỏa tài sản.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Đại kết cục viên mãn - Khởi đầu huy hoàng',
            visual: 'Hạ Vy quay lưng bước vào xe chuyên dụng. Ánh bình minh xua tan mây đen bão tố, chiếu sáng rực rỡ lên bóng hình người phụ nữ kiên cường độc lập.',
            voiceover: 'Rời bỏ kẻ không xứng đáng để tìm lại chính mình. Nữ vương đã trở lại ngai vàng, tự tay viết nên cuộc đời vĩ đại và kiêu hãnh của chính mình!',
            sound: 'Giai điệu chiến thắng hào hùng bùng nổ, khép lại trọn bộ series với cảm xúc sảng khoái tuyệt đối!'
          }
        ]
      }
    ]
  },

  // 6. THẦN Y HẠ SƠN (TRỌN BỘ 3 TẬP CHUẨN 2 PHÚT)
  {
    id: 'than-y-ha-son-huy-hon',
    title: 'Thần Y Hạ Sơn: 9 Hôn Thư Của Tài Phiệt',
    originalTitle: '神医下山: 开局退婚 (Thần Y Hạ Sơn: Khai Cục Thoái Hôn)',
    genre: 'Thần Y / Dị Năng',
    genreKey: 'than_y',
    tags: ['Thần y', 'Hạ sơn', 'Hủy hôn', 'Cứu người', 'Trọn bộ Full Arc'],
    addictionRating: 4.8,
    viewsEstimate: '1.6 Tỷ lượt xem Douyin',
    hook: 'Cầm 9 lá hôn thư xuống núi hủy hôn bị thiên kim tiểu thư ném tiền vào mặt, 5 phút sau ông cụ tài phiệt ngã gục chỉ có anh cứu được!',
    synopsis: 'Lâm Tiêu tu luyện y thuật và võ đạo tuyệt đỉnh trên núi Côn Lôn 10 năm, sư phụ phó thác 9 bức hôn thư của 9 đại gia tộc giàu có nhất thiên hạ bảo anh xuống núi hủy hôn. Sau khi cứu sống Giang lão gia từ cõi chết, đại tiểu thư kiêu ngạo hối hận bám riết theo anh, nhưng Lâm Tiêu một lòng lập lại trật tự y đạo, vạch trần âm mưu đầu độc của gia tộc đối thủ và trở thành Y Thần vạn người kính ngưỡng!',
    characters: [
      { name: 'Lâm Tiêu', role: 'Nam chính', description: 'Đệ nhất thần y Côn Lôn, tay trái châm cứu cải tử hoàn sinh, tay phải võ công vô địch.' },
      { name: 'Giang Vũ Mộng', role: 'Tiểu thư kiêu căng', description: 'Đại tiểu thư gia tộc họ Giang, mắt cao hơn đầu nhưng sau đó hối hận bám theo cầu hôn.' },
      { name: 'Giang lão gia', role: 'Bậc tiền bối', description: 'Người sáng lập tập đoàn, một lòng nhớ ơn sư phụ của Lâm Tiêu.' },
    ],
    viralHighlights: [
      'Kỹ thuật châm cứu Cửu Chuyển Hoàn Hồn Châm làm bừng sáng phòng cấp cứu.',
      'Tiểu thư quỳ gối xin tha và đòi kết hôn nhưng bị Lâm Tiêu từ chối thẳng thừng.',
      'Một chưởng đánh bay sát thủ ngoại quốc, dùng y thuật phong tỏa huyệt đạo kẻ phản đồ.',
    ],
    suggestedEpisodes: 3,
    isFullSeries: true,
    episodes: [
      {
        episodeNumber: 1,
        title: 'Tập 1: Hôn Thư Bị Xé & Ba Mũi Ngân Châm Cứu Mạng',
        hook: 'Tiểu thư nhà giàu xé nát bức hôn thư rồi giẫm giày cao gót lên vết mực!',
        cliffhanger: 'Máy đo tim hiện vạch ngang tử thần, Lâm Tiêu phóng ba mũi kim châm bạc bay vút qua không trung ghim thẳng vào huyệt đạo!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Hủy hôn ngạo mạn',
            visual: 'Giang Vũ Mộng xé vụn lá hôn thư màu đỏ ném vào thùng rác, mắt nhìn Lâm Tiêu mặc áo vải thô với ánh nhìn ghê tởm.',
            voiceover: 'Giang Vũ Mộng: "Tôi là hoa khôi thủ đô, tương lai lấy thái tử gia tộc tài phiệt. Một tên nhà quê như anh nghĩ mình xứng với tôi sao?".',
            sound: 'Tiếng xé giấy dứt khoát, tiếng cười giễu cợt của đám bạn bè xung quanh.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Lâm Tiêu thản nhiên thu dọn',
            visual: 'Lâm Tiêu mỉm cười nhẹ nhõm, cất 8 lá hôn thư còn lại vào túi vải: "Cô xé thì tốt, tôi cũng đỡ mất công từ hôn. Tạm biệt!".',
            voiceover: 'Lâm Tiêu quay lưng toan bước đi thì bỗng nhiên trong phòng khách lớn vang lên tiếng kêu cứu thảm thiết của người hầu.',
            sound: 'Tiếng đổ vỡ bát đĩa, tiếng la hét thất thanh: "Lão gia ngất rồi! Mau gọi cấp cứu!".'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Bác sĩ viện trưởng bó tay',
            visual: 'Giang lão gia nằm co giật trên sàn, môi tím tái, máy theo dõi tim kêu tít tít dồn dập. Vị bác sĩ viện trưởng lắc đầu buông tay bất lực.',
            voiceover: 'Bác sĩ: "Tắc nghẽn động mạch vành cấp tính kèm đứt mạch máu não, tim đã ngừng đập... Xin gia đình chuẩn bị hậu sự!". Giang Vũ Mộng ngã quỵ khóc nấc lên.',
            sound: 'Tiếng máy đo nhịp tim phát ra tiếng "tít... tít... tít kéo dài..." biểu thị tim ngừng đập.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Cải tử hoàn sinh chớp nhoáng',
            visual: 'Lâm Tiêu sải bước tới, một tay đẩy lùi đám vệ sĩ. Anh vung cổ tay, chiếc hộp gỗ đàn hương bật mở, ba cây ngân châm dài phát sáng trong ánh chiều tà.',
            voiceover: 'Lâm Tiêu: "Có tôi ở đây, Diêm Vương cũng không dám cướp người!". Tay anh di chuyển như múa lượn, ba mũi châm ghim sâu vào huyệt Đan Điền và Bách Hội với độ chính xác tuyệt đối.',
            sound: 'Tiếng ngân rung "ong ong" của thân kim châm bạc, tiếng hít thở nghẹt thở của toàn bộ bác sĩ.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Kỳ tích hồi sinh',
            visual: 'Một tiếng hít sâu bất ngờ vang lên! Giang lão gia mở to hai mắt, ho khan một tiếng rồi từ từ ngồi dậy trước sự bàng hoàng của cả dòng họ!',
            voiceover: 'Giang lão gia nhìn thấy Lâm Tiêu liền bật khóc nức nở, lập tức gạt tay con cháu quỳ rạp dưới chân anh: "Ân công Côn Lôn... Xin người hãy cưới cháu gái của tôi!". Tiểu thư chết lặng!',
            sound: 'Tiếng máy đo tim đập rộn ràng trở lại, nhạc cao trào vỡ òa kinh ngạc!'
          }
        ]
      },
      {
        episodeNumber: 2,
        title: 'Tập 2: Vạch Trần Âm Mưu Thuốc Độc & Tiểu Thư Hối Hận',
        hook: 'Giang Vũ Mộng quỳ xuống ôm chân Lâm Tiêu xin tha thứ, nhưng anh chỉ chỉ tay vào chén thuốc bổ trên bàn: "Đây là thuốc kịch độc!"',
        cliffhanger: 'Gã bác sĩ phản bội rút dao găm định đâm lén, Lâm Tiêu búng tay một cái bẻ gãy lưỡi thép làm đôi!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Màn hối hận của đại tiểu thư',
            visual: 'Giang Vũ Mộng bò tới trước mặt Lâm Tiêu, hai tay chắp lại van xin: "Thần y! Là em sai rồi, em ngu dốt khinh thường ngài, xin ngài đừng hủy hôn!".',
            voiceover: 'Chỉ vừa vài phút trước còn hất tiền vào mặt người ta, nay tiểu thư đệ nhất phải hạ mình quỳ lạy trước mặt chàng trai áo vải.',
            sound: 'Tiếng khóc thút thít xấu hổ, tiếng thì thầm xôn xao của đám đông.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Vạch mặt chén thuốc độc',
            visual: 'Lâm Tiêu không buồn nhìn cô ta, bước tới cầm chén thuốc bắc trên bàn ngửi nhẹ rồi đổ xuống chậu cây cảnh. Cây lập tức khô héo bốc khói đen xì!',
            voiceover: 'Lâm Tiêu: "Ông cụ không phải đột quỵ ngẫu nhiên, mà bị trúng Đoạt Mệnh Tán ngấm ngầm suốt nửa năm qua. Người dâng chén thuốc này là ai?".',
            sound: 'Tiếng xèo xèo bốc khói độc, tiếng mọi người lùi lại kinh hãi.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Kẻ chủ mưu lộ diện',
            visual: 'Vị viện trưởng thân tín của gia tộc toan lén lút lùi ra cửa sổ thì bị Giang lão gia quát lớn: "Bắt nó lại!".',
            voiceover: 'Kẻ phản bội cùng đường rút từ thắt lưng ra một con dao găm tẩm độc lao thẳng về phía Giang lão gia hòng diệt khẩu.',
            sound: 'Tiếng rút dao rít lên sắc lạnh, tiếng phụ nữ hét thất thanh.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Tuyệt kỹ điểm huyệt Côn Lôn',
            visual: 'Lâm Tiêu không thèm quay đầu lại. Hai ngón tay anh kẹp lấy mũi dao kim loại, nhẹ nhàng xoay nhẹ, lưỡi dao gãy đôi "keng" một tiếng. Một chỉ điểm vào ngực kẻ phản đồ.',
            voiceover: 'Lâm Tiêu: "Ở trước mặt tôi dám múa rìu qua mắt thợ?". Gã bác sĩ lập tức đông cứng toàn thân, ngã gục không thể cử động dù chỉ một ngón tay.',
            sound: 'Tiếng kim loại gãy đôi vang dội, tiếng rơi bịch xuống sàn.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Lệnh bài Y Thần',
            visual: 'Lâm Tiêu rút từ ngực áo ra tấm ngọc bội khắc chữ "Côn Lôn Y Thần". Toàn bộ danh y và thị trưởng vừa có mặt ngoài cửa đều run rẩy quỳ rạp chào đón.',
            voiceover: 'Thị trưởng quỳ gối: "Tham kiến Côn Lôn Y Thần đại nhân!". Giang Vũ Mộng lúc này mới nhận ra, người đàn ông cô vừa ruồng bỏ chính là vị thần bảo hộ của cả một quốc gia!',
            sound: 'Nhạc uy nghiêm rền vang, tiếng thở dài nuối tiếc cùng cực của tiểu thư!'
          }
        ]
      },
      {
        episodeNumber: 3,
        title: 'Tập 3 (Đại Kết Cục): Thiên Hạ Tôn Sùng & Lên Đỉnh Y Đạo',
        hook: '9 đại gia tộc tài phiệt xếp hàng dài cả cây số trước cổng biệt thự dâng sính lễ cầu xin Y Thần chọn làm rể!',
        cliffhanger: 'Lâm Tiêu thu hồi 9 hôn thư, tiêu sái cất bước vào đời cứu nhân độ thế, lưu danh muôn thuở!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Đoàn xe 9 đại gia tộc đổ bộ',
            visual: 'Cả con đường dẫn vào biệt thự bị tắc nghẽn bởi hàng trăm siêu xe mạ vàng. Người đứng đầu 9 đại gia tộc quyền lực nhất tề tựu đông đủ.',
            voiceover: 'Tin tức Côn Lôn Y Thần hạ sơn đã lan truyền khắp giới thượng lưu. Tất cả đều đem theo bảo vật gia truyền và 100 tỷ sính lễ mong được kết thân.',
            sound: 'Tiếng còi xe rộn rã, tiếng bước chân náo nhiệt.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Giang Vũ Mộng tự chuốc lấy cay đắng',
            visual: 'Giang Vũ Mộng đứng trong góc nhìn các tiểu thư gia tộc khác tranh nhau dâng hoa cho Lâm Tiêu mà ruột gan đau như cắt.',
            voiceover: 'Hôn thư vốn đã ở trong tay cô, nhưng chính sự kiêu căng nông cạn đã khiến cô tự tay vứt bỏ cơ hội bước lên đỉnh cao danh vọng.',
            sound: 'Nhạc bi ai châm biếm, tiếng thở dài nuối tiếc.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Thành lập Y Viện Cứu Thế',
            visual: 'Lâm Tiêu đứng trên khán đài cao, tuyên bố thành lập Trung Tâm Y Thuật Côn Lôn miễn phí chữa bệnh cho người nghèo trong thiên hạ.',
            voiceover: 'Lâm Tiêu: "Y đạo là để cứu đời, không phải để cầu vinh hoa phú quý! Bất kỳ ai có lòng hướng thiện, Côn Lôn đều sẵn lòng che chở!".',
            sound: 'Tiếng hoan hô vang dội như sấm truyền khắp phố phường.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Thu hồi hôn thư tiêu sái',
            visual: 'Lâm Tiêu thản nhiên cất lại các lá hôn thư vào túi vải, mỉm cười chào tạm biệt Giang lão gia.',
            voiceover: 'Lâm Tiêu: "Giang lão gia, ân nghĩa giữa sư phụ và ngài nay đã trả xong. Từ nay duyên phận khép lại, chúc ngài trường thọ!".',
            sound: 'Tiếng sáo trúc du dương thanh thoát.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Đại kết cục viên mãn - Tiêu sái giang hồ',
            visual: 'Lâm Tiêu sải bước trên con đường lớn, tà áo tung bay trong gió sớm, bóng lưng tự tại uy nghiêm của bậc kỳ tài.',
            voiceover: 'Xuống núi cứu thế, đập tan mưu gian, vung tay chấn hưng y đạo thiên hạ! Thần Y hạ sơn, truyền kỳ muôn đời bất hủ!',
            sound: 'Khúc ca hùng tráng vang vọng non sông, kết thúc toàn bộ series đầy khí phách!'
          }
        ]
      }
    ]
  },

  // 7. ĐOÀN SỦNG 5 ANH TRAI (TRỌN BỘ 3 TẬP CHUẨN 2 PHÚT)
  {
    id: 'doan-sung-thien-kim-5-anh-trai',
    title: 'Thiên Kim Thật Bị Đuổi: 5 Anh Trai Tỷ Phú Đến Đón',
    originalTitle: '真千金被逐: 五个大佬哥哥杀疯了 (Chân Thiên Kim Bị Trục: Ngũ Ca Ca Sát Phong Liễu)',
    genre: 'Đoàn Sủng / Thiên Kim',
    genreKey: 'doan_sung',
    tags: ['Đoàn sủng', '5 anh trai đại lão', 'Gia tộc hối hận', 'Vả mặt', 'Trọn bộ Full Arc'],
    addictionRating: 4.9,
    viewsEstimate: '2.3 Tỷ lượt xem Douyin',
    hook: 'Bị gia đình nuôi vứt đồ ra đường giữa cơn mưa bão, 5 phút sau đoàn xe 50 chiếc Rolls-Royce hộ tống 5 anh trai tỷ phú tới rước em gái!',
    synopsis: 'An An bị gia đình cha mẹ nuôi ngược đãi, bóc lột sức lao động để nuôi con trai cưng của họ. Ngày sinh nhật 18 tuổi, cô bị đuổi ra khỏi nhà giữa trời mưa tuyết lạnh giá chỉ vì làm vỡ chiếc bát mẻ. Nhưng họ không hề biết, gia đình thực sự của An An là gia tộc đứng đầu thế giới! Năm người anh ruột lập tức phong tỏa toàn thành phố đón cô về, tịch thu toàn bộ tài sản của gia đình nuôi và mở tiệc thượng lưu tuyên bố em gái là người thừa kế duy nhất!',
    characters: [
      { name: 'An An', role: 'Nữ chính bé nhỏ', description: 'Cô gái ngoan ngoãn thuần khiết, chịu đựng bất hạnh từ nhỏ nhưng nay trở thành viên ngọc quý.' },
      { name: 'Đại ca - Cố Lẫm', role: 'Trùm tài phiệt', description: 'Chủ tịch tập đoàn tài chính đa quốc gia, sẵn sàng chi tiền mua cả thành phố cho em gái chơi.' },
      { name: 'Nhị ca - Cố Tiêu', role: 'Chiến tướng quân đội', description: 'Uy quyền ngập trời, kẻ nào làm em gái khóc sẽ bị san phẳng.' },
      { name: 'Bà mẹ nuôi tham lam', role: 'Phản diện', description: 'Bóc lột con nuôi, sau khi biết thân phận thật thì quỳ lạy xin nhận người thân.' },
    ],
    viralHighlights: [
      'Đoàn xe 50 chiếc Rolls-Royce dừng kín cả con hẻm nghèo.',
      '5 người anh trai quỳ xuống che dù và đi giày ấm cho em gái.',
      'Tịch thu toàn bộ sổ đỏ của gia đình nuôi, đưa em gái về sống trong lâu đài triệu đô.',
    ],
    suggestedEpisodes: 3,
    isFullSeries: true,
    episodes: [
      {
        episodeNumber: 1,
        title: 'Tập 1: Mưa Lạnh Bị Đuổi & 5 Đại Lão Đổ Bộ',
        hook: 'Mẹ nuôi ném chiếc túi xách rách nát của An An xuống vũng nước mưa!',
        cliffhanger: 'Tiếng còi hú của 50 chiếc siêu xe vang vọng cả khu phố, 5 người đàn ông vĩ đại nhất đất nước đồng loạt bước xuống che dù cho cô!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Bị đuổi ra đường giữa mưa bão',
            visual: 'Mưa tầm tã rơi trên con ngõ nhỏ. Người phụ nữ dữ dằn đẩy cô gái nhỏ nhắn mặc áo mỏng tang ngã xuống vũng bùn bẩn, ném đồ đạc ra sau.',
            voiceover: 'Mẹ nuôi cay nghiệt: "Cút đi! Nuôi mày tốn cơm tốn gạo, nay anh mày cần tiền cưới vợ, mày không chịu gả cho lão già 60 tuổi đầu xóm thì ra đường mà chết đói!".',
            sound: 'Tiếng sấm sét chớp giật ầm ầm, tiếng mưa rơi xối xả.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Sự kiên cường trong nước mắt',
            visual: 'An An ôm chặt lấy tấm ảnh cũ của người mẹ đã mất, đôi môi tím tái vì lạnh nhưng ánh mắt kiên định không rơi một giọt nước mắt van xin.',
            voiceover: 'An An run rẩy: "Tiền công 10 năm qua con đi làm thêm đưa hết cho mẹ, coi như con trả hết ân nghĩa. Từ nay con không còn là người nhà này nữa!".',
            sound: 'Tiếng gió bão hú gào rít từng cơn.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Ánh đèn pha xé toạc bóng đêm',
            visual: 'Cả khu ngõ bỗng sáng rực như ban ngày. Tiếng động cơ siêu xe gầm rú rung chuyển mặt đất. Hàng chục chiếc Rolls-Royce Phantom đen bóng nối đuôi nhau xếp kín cả con phố!',
            voiceover: 'Mẹ nuôi và gã con trai há hốc mồm sợ hãi: "Chuyện gì thế này? Đoàn xe của quan chức cấp cao nào đi lạc vào đây à?". Nhưng đoàn xe đều dừng ngay trước mặt An An!',
            sound: 'Tiếng còi xe đặc chủng dẹp đường, tiếng lốp xe nghiến mặt đường đanh thép.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Năm chiếc ô đen mở ra',
            visual: 'Năm cánh cửa xe mở tung. Năm người đàn ông cao lớn mặc quân phục và âu phục sang trọng bậc nhất đồng loạt lao xuống mưa, quên cả che ô cho chính mình mà cùng chạy tới quỳ xuống trước mặt cô gái nhỏ.',
            voiceover: 'Đại ca Cố Lẫm ôm chặt lấy cô bé, giọng nghẹn ngào rơi nước mắt: "An An... Em gái nhỏ của các anh! Bọn anh tìm em suốt 15 năm qua, để em phải chịu khổ rồi!".',
            sound: 'Tiếng nhạc dương cầm xúc động chuyển sang giai điệu hào hùng dâng trào.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Ánh mắt trừng phạt',
            visual: 'Nhị ca Cố Tiêu đứng dậy, quân hàm Thống Đốc lấp lánh dưới mưa, ánh mắt nhìn thẳng vào gia đình nuôi độc ác như nhìn những xác chết.',
            voiceover: 'Cố Tiêu ra lệnh dứt khoát: "Đội Vệ Binh! Phong tỏa khu vực này. Kẻ nào dám làm em gái ta phải ướt mưa... Lập tức bắt giữ điều tra toàn bộ nguồn tiền bất chính!". Mặt mẹ nuôi tái ngắt ngã xỉu!',
            sound: 'Tiếng nạp đạn của vệ binh, tiếng gào khóc xin tha bị cắt ngang dứt khoát!'
          }
        ]
      },
      {
        episodeNumber: 2,
        title: 'Tập 2: Thu Hồi Căn Nhà & Vả Mặt Cả Dòng Họ Bất Nhân',
        hook: 'Gã con trai cưng của mẹ nuôi lao ra đòi đánh các anh trai, bị vệ sĩ khóa tay ấn đầu xuống vũng nước bùn!',
        cliffhanger: 'Đại ca ném ra xấp giấy tờ nợ nần: "Căn nhà các người đang ở là do em gái ta kiếm tiền mua, hôm nay tịch thu toàn bộ!"',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Kẻ bất tài giở thói côn đồ',
            visual: 'Gã con trai cưng cầm cây gậy sắt lao ra chửi bới: "Lũ chó nào dám cản trở chuyện cưới xin của tao?". Vệ sĩ áo đen chỉ dùng một tay tóm chặt gậy, bẻ gập cổ tay hắn.',
            voiceover: 'Một tiếng rắc vang lên! Gã con trai la hét quỳ sụp xuống vũng bùn trước mặt các vị tướng lĩnh uy nghi.',
            sound: 'Tiếng khớp xương rắc, tiếng la hét thảm thiết của gã con trai cưng.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Mẹ nuôi trơ trẽn xin tiền chuộc',
            visual: 'Bà mẹ nuôi bò tới van lạy Cố Lẫm: "Các ông là người giàu có, muốn đón con bé đi thì phải đền bù công nuôi dưỡng 50 tỷ cho tôi!".',
            voiceover: 'Đại ca Cố Lẫm khinh bỉ nhìn mụ đàn bà tham lam: "Công nuôi dưỡng? Suốt 10 năm qua em gái tôi đi làm từ 5 giờ sáng, tiền viện phí, tiền mua nhà đều từ mồ hôi nước mắt của em ấy!".',
            sound: 'Tiếng cười khinh miệt của 5 người anh trai.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Tịch thu toàn bộ tài sản bất chính',
            visual: 'Tam ca - Luật sư đứng đầu quốc gia rút ra bản kê khai tài chính và lệnh niêm phong nhà đất của Tòa án Tối cao.',
            voiceover: 'Tam ca: "Căn nhà này đứng tên người được ủy thác là An An. Trong vòng 10 phút, tất cả đồ đạc của các người phải dọn sạch ra khỏi đây!".',
            sound: 'Tiếng con dấu đỏ đóng cộp lên lệnh niêm phong.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Sự sụp đổ của gia đình nuôi',
            visual: 'Chủ nợ cờ bạc của gã con trai cũng vừa kéo tới siết nợ. Hai mẹ con bị đuổi ra đường trong sự cười chê của toàn bộ hàng xóm láng giềng.',
            voiceover: 'Gieo nhân nào gặt quả nấy. Kẻ từng nhẫn tâm đuổi cô bé 18 tuổi ra mưa, nay chính mình phải nếm trải cảnh không nơi nương tựa giữa đêm đông!',
            sound: 'Tiếng khóc lóc kêu trời, tiếng hàng xóm xì xào thỏa mãn.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Khăn ấm và xe hoa',
            visual: 'Tứ ca - Siêu sao âm nhạc choàng chiếc áo lông chồn trắng tinh ấm áp lên vai An An, ngũ ca - Vua đua xe mở cửa chiếc xe hoa màu hồng cổ tích.',
            voiceover: 'Năm người anh trai đồng thanh: "An An, về nhà thôi! Từ nay em chính là tiểu công chúa độc nhất vô nhị của gia tộc Cố thị!". Khung hình dừng lại ở nụ cười rạng rỡ của An An.',
            sound: 'Nhạc cổ tích ngọt ngào ấm áp vang lên làm tan chảy mọi trái tim!'
          }
        ]
      },
      {
        episodeNumber: 3,
        title: 'Tập 3 (Đại Kết Cục): Đại Tiệc Thừa Kế & Công Chúa Hào Môn',
        hook: 'Toàn bộ giới tài phiệt thủ đô cúi đầu trước cô công chúa nhỏ lộng lẫy trong chiếc vương miện kim cương 500 carat!',
        cliffhanger: '5 người anh trai tuyên bố: "Ai dám làm An An buồn, 5 tập đoàn lớn nhất nước sẽ hợp lực san phẳng!"',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Lâu đài nguy nga rực rỡ',
            visual: 'Khung cảnh lâu đài cổ tích Cố gia tráng lệ giữa thủ đô. Thảm đỏ trải dài hàng trăm mét chào đón tiểu thư duy nhất của gia tộc trở về.',
            voiceover: 'Một buổi tiệc thượng lưu hoành tráng chưa từng có được tổ chức để chính thức công bố danh phận người thừa kế gia tộc Cố thị.',
            sound: 'Tiếng kèn đồng hoàng gia ngân vang, tiếng pháo hoa rực rỡ ban ngày.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Màn lột xác thành công chúa',
            visual: 'An An bước xuống từ cầu thang xoắn ốc lộng lẫy trong bộ váy dạ hội công chúa đính hàng vạn viên kim cương Swarovski, nhan sắc thuần khiết toát lên vẻ quý phái ngút ngàn.',
            voiceover: 'Không còn là cô bé lam lũ ướt mưa ngày nào, nay An An là bảo bối được 5 đại lão nâng như nâng trứng, hứng như hứng hoa.',
            sound: 'Tiếng trầm trồ kinh ngạc của hàng ngàn quan khách thượng lưu.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Món quà ra mắt của 5 anh trai',
            visual: '5 người anh trai lần lượt bước lên trao quà: Đảo riêng Thái Bình Dương, Hạm đội du thuyền, 50% cổ phần tập đoàn tài chính và tòa tháp cao nhất thành phố.',
            voiceover: 'Đại ca Cố Lẫm dõng dạc tuyên bố: "Toàn bộ tài sản này đều đứng tên một mình em gái Cố An An! Kẻ nào dám có ý đồ xấu với em gái ta, hãy tự chuẩn bị hậu sự!".',
            sound: 'Tiếng vỗ tay vang dội như sấm truyền khắp khán phòng.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Gia đình nuôi hối hận quỳ ngoài cổng',
            visual: 'Mẹ nuôi và gã con trai rách rưới đứng quỳ ngoài cổng lâu đài xa hoa, nhìn qua màn hình LED thấy An An rạng ngời mà đấm ngực hối hận đến hộc máu.',
            voiceover: 'Từng có cơ hội làm mẹ của vị thiên kim giàu nhất thế giới, nhưng lòng tham và sự độc ác đã tước đoạt tất cả của mụ đàn bà bất nhân.',
            sound: 'Nhạc đối lập giữa xa hoa bên trong và thê thảm bên ngoài.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Đại kết cục viên mãn - Hạnh phúc trọn đời',
            visual: 'Năm người anh trai cùng ôm lấy An An giữa pháo hoa rực rỡ trên bầu trời lâu đài. Nụ cười hạnh phúc nở trên môi cô bé.',
            voiceover: 'Khổ tận cam lai! Sau những năm tháng bão giông, viên ngọc quý đã trở về với vị trí thuộc về mình, được yêu thương và bảo bọc đến trọn đời!',
            sound: 'Khúc ca chiến thắng và tình cảm gia đình vang dội, khép lại trọn bộ series đầy thỏa mãn và xúc động!'
          }
        ]
      }
    ]
  }
];
