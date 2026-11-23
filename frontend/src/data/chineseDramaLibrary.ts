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
  {
    id: 'zhan-shen-long-te',
    title: 'Chiến Thần Ở Rể: 100 Tỷ Đô Xuất Kích',
    originalTitle: '战神龙婿: 赘婿逆袭 (Chiến Thần Long Tế)',
    genre: 'Chiến Thần / Ở Rể',
    genreKey: 'zhan_shen',
    tags: ['Vả mặt', 'Ở rể', 'Thân phận thật', 'Long Vương', 'Gia tộc khinh bỉ'],
    addictionRating: 5.0,
    viewsEstimate: '2.4 Tỷ lượt xem Douyin',
    hook: 'Làm rể 3 năm quỳ rửa chân cho mẹ vợ, ngày thượng thọ bị đuổi ra đường, 10 chiếc trực thăng quân đoàn bất ngờ hạ cánh!',
    synopsis: 'Diệp Thần vì lời thề với sư phụ mà ẩn thân làm rể hèn nhà họ Lâm suốt 3 năm, chịu đủ trăm bề nhục nhã. Đúng ngày đại thọ 70 của lão thái thái, mẹ vợ ép anh ký đơn ly hôn để gả vợ anh cho thiếu gia tập đoàn họ Vương. Khi hợp đồng hôn nhân vừa xé, 10 chiếc trực thăng vũ trang của Chiến Thần Điện bất ngờ phủ kín bầu trời thành phố, vạn binh sĩ quỳ rạp: "Kính chào Long Vương trở về!".',
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
        title: 'Tập 3: Mẹ Vợ Quỳ Lạy & Ân Đoạn Nghĩa Tuyệt',
        hook: 'Mẹ vợ bò lết dưới sàn ôm chặt ống quần Diệp Thần, khóc lóc van xin tha thứ!',
        cliffhanger: 'Diệp Thần nắm tay Thanh Uyển bước lên chuyên cơ, để lại cả gia tộc chìm trong biển hối hận khôn nguôi!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Hook tập 3 - Sự sụp đổ của kẻ phản diện',
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
            title: 'Đại kết cục tập phim - Khởi hành đỉnh cao',
            visual: 'Hai người sải bước ra thảm đỏ dẫn lên chuyên cơ Long Vương. Phía sau lưng, cả gia tộc họ Lâm đập đầu van xin xin tha thứ nhưng cánh cửa phi cơ đã khép lại.',
            voiceover: 'Cánh cửa đóng lại, động cơ gầm vang. Một hành trình mới bắt đầu: Long Vương tái xuất, thiên hạ khuynh đảo!',
            sound: 'Tiếng phản lực xé gió bay lên bầu trời xanh, nhạc chiến thắng vang dội đầy thỏa mãn!'
          }
        ]
      }
    ]
  },
  {
    id: 'trung-sinh-bao-thu-thien-kim',
    title: 'Trùng Sinh Báo Thù: Thiên Kim Thật Trở Về',
    originalTitle: '真假千金: 重生复仇 (Chân Giả Thiên Kim: Trọng Sinh Phục Thù)',
    genre: 'Trùng Sinh / Báo Thù',
    genreKey: 'trung_sinh',
    tags: ['Trùng sinh', 'Vả mặt trà xanh', 'Thiên kim thật', 'Hào môn', 'Báo thù'],
    addictionRating: 4.9,
    viewsEstimate: '1.8 Tỷ lượt xem Douyin',
    hook: 'Kiếp trước bị trà xanh hãm hại lấy tủy rồi ném xuống biển, kiếp này trùng sinh đúng ngày xét nghiệm ADN nhận thân!',
    synopsis: 'Kiếp trước, Ninh Duệ là thiên kim thật của gia tộc giàu có nhất thủ đô nhưng bị cô em nuôi trà xanh vu oan giá họa, cha mẹ ruột ruồng bỏ ép hiến tủy cho con nuôi rồi chết thảm nơi đáy biển lạnh giá. Trùng sinh về năm 20 tuổi đúng buổi tiệc công bố kết quả ADN, Ninh Duệ mang theo bản báo cáo pháp y quốc tế, vạch trần âm mưu tráo đổi con gái và liên minh với vị hôn phu quyền lực nhất Bắc Kinh để vả mặt từng kẻ phản bội!',
    characters: [
      { name: 'Ninh Duệ', role: 'Nữ chính', description: 'Trùng sinh mang theo ký ức và tài năng y học thiên tài, quyết tâm đòi lại tất cả.' },
      { name: 'Ninh Tuyết', role: 'Trà xanh phản diện', description: 'Con gái nuôi giả tạo, bề ngoài ngây thơ yếu đuối nhưng lòng dạ độc như rắn rết.' },
      { name: 'Phó Tư Viễn', role: 'Nam chính', description: 'Đại lão đứng đầu giới thương trường Bắc Kinh, lạnh lùng tàn nhẫn nhưng sủng vợ vô điều kiện.' },
      { name: 'Bà Ninh', role: 'Mẹ ruột u mê', description: 'Mù quáng tin tưởng con nuôi, coi thường con gái ruột bị thất lạc ở nông thôn.' },
    ],
    viralHighlights: [
      'Ném bản xét nghiệm ADN thật và đoạn ghi âm tráo đổi con vào máy chiếu hội nghị.',
      'Trà xanh quỳ gối xin tha nhưng bị tát liên tiếp hai bạt tai vang dội.',
      'Tổng tài Phó Tư Viễn xuất hiện tuyên bố: "Ai dám đụng vào vợ tôi, chuẩn bị mua đất nghĩa trang!".',
    ],
    suggestedEpisodes: 3,
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
      }
    ]
  },
  {
    id: 'gia-ngheo-xem-mat-nu-tong-tai',
    title: 'Giả Nghèo Đi Xem Mắt Gặp Ngay Nữ Tổng Tài',
    originalTitle: '装穷相亲: 闪婚万亿女总裁 (Trang Cùng Tương Thân: Thiểm Hôn Nữ Tổng Tài)',
    genre: 'Giả Nghèo / Xem Mắt',
    genreKey: 'gia_ngheo',
    tags: ['Giả nghèo', 'Xem mắt', 'Chủ tịch giấu nghề', 'Hài hước', 'Vả mặt'],
    addictionRating: 4.9,
    viewsEstimate: '2.1 Tỷ lượt xem Douyin',
    hook: 'Chủ tịch tập đoàn nghìn tỷ mặc áo shipper đi xem mắt bị gái thực dụng tạt nước, nữ chủ tịch bàn bên kéo tay đi đăng ký kết hôn!',
    synopsis: 'Lục Phong - chủ tịch tập đoàn công nghệ Lục Thị nghìn tỷ, vì chán ngấy những cô nàng đào mỏ nên quyết định mặc đồ shipper đi xem mắt. Bị cô gái thực dụng mắng chửi thậm tệ và đòi tiền đền bù thời gian, đúng lúc này, nữ tổng tài số 1 thành phố Thẩm Nhược Hy ngồi bàn bên cạnh bỗng đứng dậy: "Anh này em lấy! Bây giờ mang sổ hộ khẩu đi đăng ký kết hôn luôn!". Kể từ đó là chuỗi ngày vừa giả nghèo vừa cưng chiều vợ tổng tài, tiện tay vả mặt từng kẻ coi thường!',
    characters: [
      { name: 'Lục Phong', role: 'Nam chính', description: 'Chủ tịch tỷ đô ẩn thân làm shipper để tìm kiếm chân ái đời mình.' },
      { name: 'Thẩm Nhược Hy', role: 'Nữ chính', description: 'Nữ chủ tịch xinh đẹp khí chất ngời ngời, đang cần tìm người kết hôn gấp để đối phó gia tộc ép hôn.' },
      { name: 'Vương Mỹ Lệ', role: 'Gái đào mỏ', description: 'Đối tượng xem mắt thực dụng, coi thường người nghèo.' },
      { name: 'Lý thiếu', role: 'Công tử phản diện', description: 'Theo đuổi Thẩm Nhược Hy bất thành, luôn tìm cách bêu rếu Lục Phong.' },
    ],
    viralHighlights: [
      'Gái thực dụng đòi quà 1 tỷ bị bóc phốt hóa đơn thuê túi hiệu.',
      'Nữ tổng tài kéo tay shipper đi đăng ký kết hôn trước mắt cả nhà hàng.',
      'Mở cốp xe máy cà tàng nhưng bên trong là chìa khóa siêu xe Pagani và hợp đồng mua lại toàn bộ khu phố thương mại.',
    ],
    suggestedEpisodes: 3,
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
      }
    ]
  },
  {
    id: 'me-ke-manh-bao-trieu-do',
    title: 'Mẹ Kế Độc Ác và Hài Nhi Thiên Tài Triệu Đô',
    originalTitle: '后妈恶毒: 天才萌宝找爹地 (Hậu Ma Ác Độc: Thiên Tài Manh Bảo)',
    genre: 'Mẹ Kế / Manh Bảo',
    genreKey: 'manh_bao',
    tags: ['Manh bảo thiên tài', 'Mẹ kế độc ác', 'Tổng tài tìm vợ', 'Hài hước', 'Gia đấu'],
    addictionRating: 5.0,
    viewsEstimate: '2.5 Tỷ lượt xem Douyin',
    hook: 'Hai nhóc tì 5 tuổi hack toàn bộ màn hình LED tòa nhà chọc trời để bóc phốt mẹ kế độc ác và tìm bố đền mẹ!',
    synopsis: '5 năm trước cô bị mẹ kế và em gái hãm hại mang thai đôi rồi bị đuổi ra nước ngoài trong tủi nhục. 5 năm sau dắt 2 bảo bối thiên tài về nước: bé anh là hacker đứng đầu tổ chức ngầm quốc tế, bé em là thần đồng đầu tư chứng khoán triệu đô. Vừa về tới sân bay, hai nhóc phát hiện bố mình sắp cưới ả mẹ kế độc ác, liền lập tức tấn công hệ thống màn hình LED toàn thành phố để phát tán bằng chứng tội ác!',
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
      }
    ]
  },
  {
    id: 'vo-cu-tro-ve-tong-tai-hoi-han',
    title: 'Ly Hôn Rồi, Tổng Tài Mới Biết Vợ Cũ Là Đại Lão',
    originalTitle: '离婚后前夫追妻火葬场 (Ly Hôn Hậu Tiền Phu Truy Thê Hỏa Táng Tràng)',
    genre: 'Vợ Cũ Đại Lão / Ngược',
    genreKey: 'nguoc_luyen',
    tags: ['Vợ cũ trở về', 'Hối hận', 'Truy thê', 'Đại lão ẩn danh', 'Ngược nam chính'],
    addictionRating: 4.8,
    viewsEstimate: '1.9 Tỷ lượt xem Douyin',
    hook: 'Vừa ký đơn ly hôn dứt áo ra đi, ngày hôm sau cô xuất hiện trên thảm đỏ với tư cách chủ tịch tập đoàn đối thủ!',
    synopsis: 'Hạ Vy vì yêu Mộ Kính Thần mà nhẫn nhịn làm người vợ hiền thục suốt 3 năm, chịu đựng sự lạnh nhạt và lời sỉ nhục từ gia đình chồng. Khi bạch nguyệt quang của anh ta trở về, cô dứt khoát ký đơn ly hôn không lấy một xu. Ngày hôm sau, trong buổi lễ ký kết dự án 50 tỷ đô, Mộ tổng kinh hoàng khi thấy vị phu nhân ngoan ngoãn ngày nào đang ngồi ghế chủ tọa, thân phận thật là người sáng lập tập đoàn tài chính lớn nhất châu lục!',
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
      }
    ]
  },
  {
    id: 'than-y-ha-son-huy-hon',
    title: 'Thần Y Hạ Sơn: 9 Hôn Thư Của Tài Phiệt',
    originalTitle: '神医下山: 开局退婚 (Thần Y Hạ Sơn: Khai Cục Thoái Hôn)',
    genre: 'Thần Y / Dị Năng',
    genreKey: 'than_y',
    tags: ['Thần y', 'Hạ sơn', 'Hủy hôn', 'Cứu người', 'Vả mặt'],
    addictionRating: 4.8,
    viewsEstimate: '1.6 Tỷ lượt xem Douyin',
    hook: 'Cầm 9 lá hôn thư xuống núi hủy hôn bị thiên kim tiểu thư ném tiền vào mặt, 5 phút sau ông cụ tài phiệt ngã gục chỉ có anh cứu được!',
    synopsis: 'Lâm Tiêu tu luyện y thuật và võ đạo tuyệt đỉnh trên núi Côn Lôn 10 năm, sư phụ phó thác 9 bức hôn thư của 9 đại gia tộc giàu có nhất thiên hạ bảo anh xuống núi hủy hôn. Vừa tới biệt thự họ Giang, đại tiểu thư kiêu ngạo đã ném xấp tiền 100 triệu bảo anh cút về núi. Đúng lúc này lão thái gia lên cơn nhồi máu cơ tim thập tử nhất sinh, mọi danh y bó tay, Lâm Tiêu chỉ dùng 3 cây ngân châm kéo ông cụ từ cõi chết trở về!',
    characters: [
      { name: 'Lâm Tiêu', role: 'Nam chính', description: 'Đệ nhất thần y Côn Lôn, tay trái châm cứu cải tử hoàn sinh, tay phải võ công vô địch.' },
      { name: 'Giang Vũ Mộng', role: 'Tiểu thư kiêu căng', description: 'Đại tiểu thư gia tộc họ Giang, mắt cao hơn đầu nhưng sau đó hối hận bám theo cầu hôn.' },
      { name: 'Giang lão gia', role: 'Bậc tiền bối', description: 'Người sáng lập tập đoàn, một lòng nhớ ơn sư phụ của Lâm Tiêu.' },
    ],
    viralHighlights: [
      'Kỹ thuật châm cứu Cửu Chuyển Hoàn Hồn Châm làm bừng sáng phòng cấp cứu.',
      'Tiểu thư quỳ gối xin tha và đòi kết hôn nhưng bị Lâm Tiêu từ chối thẳng thừng.',
    ],
    suggestedEpisodes: 3,
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
      }
    ]
  },
  {
    id: 'doan-sung-thien-kim-5-anh-trai',
    title: 'Thiên Kim Thật Bị Đuổi: 5 Anh Trai Tỷ Phú Đến Đón',
    originalTitle: '真千金被逐: 五个大佬哥哥杀疯了 (Chân Thiên Kim Bị Trục: Ngũ Ca Ca Sát Phong Liễu)',
    genre: 'Đoàn Sủng / Thiên Kim',
    genreKey: 'doan_sung',
    tags: ['Đoàn sủng', '5 anh trai đại lão', 'Gia tộc hối hận', 'Vả mặt', 'Ngọt ngào'],
    addictionRating: 4.9,
    viewsEstimate: '2.3 Tỷ lượt xem Douyin',
    hook: 'Bị gia đình nuôi vứt đồ ra đường giữa cơn mưa bão, 5 phút sau đoàn xe 50 chiếc Rolls-Royce hộ tống 5 anh trai tỷ phú tới rước em gái!',
    synopsis: 'An An bị gia đình cha mẹ nuôi ngược đãi, bóc lột sức lao động để nuôi con trai cưng của họ. Ngày sinh nhật 18 tuổi, cô bị đuổi ra khỏi nhà giữa trời mưa tuyết lạnh giá chỉ vì làm vỡ chiếc bát mẻ. Nhưng họ không hề biết, gia đình thực sự của An An là gia tộc đứng đầu thế giới đang ráo riết tìm kiếm cô suốt 15 năm qua! Năm người anh ruột gồm: Trùm tài phiệt, Thống đốc quân đội, Viện trưởng y khoa, Vua đua xe F1 và Siêu sao âm nhạc quốc tế lập tức phong tỏa toàn thành phố để đón tiểu công chúa về cung phụng!',
    characters: [
      { name: 'An An', role: 'Nữ chính bé nhỏ', description: 'Cô gái ngoan ngoãn thuần khiết, chịu đựng bất hạnh từ nhỏ nhưng nay trở thành viên ngọc quý.' },
      { name: 'Đại ca - Cố Lẫm', role: 'Trùm tài phiệt', description: 'Chủ tịch tập đoàn tài chính đa quốc gia, sẵn sàng chi tiền mua cả thành phố cho em gái chơi.' },
      { name: 'Nhị ca - Cố Tiêu', role: 'Chiến tướng quân đội', description: 'Uy quyền ngập trời, kẻ nào làm em gái khóc sẽ bị san phẳng.' },
      { name: 'Bà mẹ nuôi tham lam', role: 'Phản diện', description: 'Bóc lột con nuôi, sau khi biết thân phận thật thì quỳ lạy xin nhận người thân.' },
    ],
    viralHighlights: [
      'Đoàn xe 50 chiếc Rolls-Royce dừng kín cả con hẻm nghèo.',
      '5 người anh trai quỳ xuống che dù và đi giày ấm cho em gái.',
      'Ngay lập tức tịch thu toàn bộ tài sản của gia đình nuôi bất nhân.',
    ],
    suggestedEpisodes: 3,
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
      }
    ]
  }
];
