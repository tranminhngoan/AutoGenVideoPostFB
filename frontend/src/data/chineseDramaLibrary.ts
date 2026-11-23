export interface DramaCharacter {
  name: string;
  role: string; // e.g. "Nam chính", "Nữ chính", "Má vợ chua ngoa", "Trà xanh"
  description: string;
}

export interface DramaScene {
  sceneNumber: number;
  timecode: string; // e.g. "0:00 - 0:15"
  title: string;
  visual: string; // Prompt / mô tả hình ảnh cho AI video / quay dựng (bối cảnh Việt Nam)
  voiceover: string; // Lời dẫn / lời thoại lồng tiếng Việt chuẩn cảm xúc
  sound: string; // SFX, BGM, âm thanh sông nước / kịch tính
}

export interface DramaEpisode {
  episodeNumber: number;
  title: string;
  hook: string; // Điểm giật gân mở đầu (5s)
  cliffhanger: string; // Cú lật / cắt tập gay cấn nối sang tập sau
  durationSeconds: number; // ~120s (2 phút)
  scenes: DramaScene[];
  notes?: string;
}

export interface ChineseDrama {
  id: string;
  title: string;
  originalTitle: string; // Tên gốc Trung Quốc tham khảo
  vietnameseSetting: string; // Bối cảnh Việt Nam (Miền Tây, Sài Gòn, Bến Tre, Bạc Liêu...)
  genre: string;
  genreKey: 'mien_tay' | 'zhan_shen' | 'tong_tai' | 'trung_sinh' | 'manh_bao' | 'gia_ngheo' | 'nguoc_luyen' | 'than_y' | 'doan_sung';
  tags: string[];
  addictionRating: number; // 4.8 - 5.0
  viewsEstimate: string;
  hook: string;
  synopsis: string;
  characters: DramaCharacter[];
  viralHighlights: string[];
  suggestedEpisodes: number;
  isFullSeries: boolean;
  episodes: DramaEpisode[];
}

export const DRAMA_GENRES = [
  { key: 'all', label: 'Tất cả thể loại' },
  { key: 'mien_tay', label: '🌾 Miền Tây Sông Nước' },
  { key: 'zhan_shen', label: '🎖️ Rể Nghèo Nghịch Tập' },
  { key: 'tong_tai', label: '💼 Tổng Tài / Hào Môn' },
  { key: 'trung_sinh', label: '⏳ Trùng Sinh Báo Thù' },
  { key: 'manh_bao', label: '👶 Manh Bảo / Hài Nhi' },
  { key: 'gia_ngheo', label: '🛵 Giả Nghèo Coi Mắt' },
  { key: 'nguoc_luyen', label: '💔 Vợ Cũ Đại Lão' },
  { key: 'than_y', label: '🌿 Thần Y Bảy Núi' },
  { key: 'doan_sung', label: '👑 Đoàn Sủng Tiểu Thư' },
] as const;

export const CHINESE_DRAMAS: ChineseDrama[] = [
  // 1. CHÀNG RỂ MIỀN TÂY (BỐI CẢNH SÔNG TIỀN - CẦN THƠ - BẾN TRE)
  {
    id: 'chang-re-mien-tay-100-ty',
    title: 'Chàng Rể Miền Tây: Gia Tộc 100 Tỷ Về Làng',
    originalTitle: 'Gốc TQ: 战神龙婿: 赘婿逆袭 (Chiến Thần Long Tế)',
    vietnameseSetting: 'Miền Tây sông nước: Bến đò sông Hậu, miệt vườn sầu riêng Cái Mơn & Biệt phủ họ Lê',
    genre: 'Miền Tây Sông Nước',
    genreKey: 'mien_tay',
    tags: ['Miền Tây', 'Ở rể', 'Vả mặt', 'Thân phận thật', 'Sông Tiền sông Hậu', 'Trọn bộ Full Arc'],
    addictionRating: 5.0,
    viewsEstimate: '3.2 Tỷ lượt xem Douyin/TikTok',
    hook: 'Làm rể miền Tây 3 năm rửa chân chèo ghe cho má vợ, ngày giỗ chạp bị ép ly hôn, 10 chiếc tàu cao tốc bọc thép cùng trực thăng phủ kín ngã ba sông Hậu!',
    synopsis: 'Lâm Hoàng Hải vì lời thề với sư phụ mà ẩn thân làm rể hèn nhà họ Lê ở miệt vườn Bến Tre suốt 3 năm, ngày ngày chèo ghe, tưới sầu riêng, chịu đủ lời sỉ nhục từ má vợ cay nghiệt. Đúng ngày đại tiệc giỗ cụ tổ dòng họ, má vợ ép anh ký đơn ly dị để gả vợ anh cho công tử Huỳnh Phú - chủ mỏ cát giàu nhất xứ Tiền Giang. Khi tờ đơn ly dị vừa xé toạc, 10 chiếc cano bọc thép cùng đoàn trực thăng của Quân Khu Phương Nam bất ngờ đổ bộ, hàng ngàn vệ binh bồng súng quỳ rạp: "Cung nghênh Long Soái trở về!".',
    characters: [
      { name: 'Lâm Hoàng Hải', role: 'Nam chính', description: 'Long Soái thống lĩnh Vùng 4, đại gia tộc lúa gạo thủy sản Biển Đông giả nghèo làm rể báo ân.' },
      { name: 'Lê Thanh Thảo', role: 'Nữ chính', description: 'Con gái út nhà họ Lê, nết na thùy mị, một lòng thương chồng dù gia đình khinh rẻ.' },
      { name: 'Bà Ba Đạt', role: 'Má vợ cay nghiệt', description: 'Tham tiền mê bạc, hách dịch coi thường rể miệt vườn.' },
      { name: 'Công tử Huỳnh Phú', role: 'Thiếu gia phản diện', description: 'Con trai trùm mỏ cát sông Tiền, cậy tiền ép bức người lương thiện.' },
    ],
    viralHighlights: [
      'Má vợ hất xô nước rửa chén vào chân rể nghèo giữa sảnh tiệc giỗ tổ.',
      'Cảnh xé đơn ly hôn và ném thẳng vào mặt thiếu gia vựa cát: "Gia tộc Lê này... tôi không thèm!".',
      'Đoàn cano bọc thép xé sóng sông Hậu, trực thăng hạ cánh bốc bay bàn tiệc, má vợ ngất xỉu tại chỗ.',
    ],
    suggestedEpisodes: 3,
    isFullSeries: true,
    episodes: [
      {
        episodeNumber: 1,
        title: 'Tập 1: Tiệc Giỗ Cụ Tổ & Tiếng Sóng Thần Trên Sông Hậu',
        hook: 'Bà Ba Đạt hất thẳng xô nước rửa chén lên đôi ủng cao su của Hoàng Hải ngay trước mặt cả họ tộc!',
        cliffhanger: 'Động cơ trực thăng và còi hú của 10 cano bọc thép vang dội cả khúc sông, vệ binh bồng súng ùa vào sân đình!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Sỉ nhục giữa tiệc họ tộc miền Tây',
            visual: 'Cận cảnh xô nước rửa chén nhớp nhúa văng tung tóe lên đôi ủng cũ của Hoàng Hải. Bà Ba Đạt chỉ thẳng ngón tay đeo nhẫn vàng vào trán anh, xung quanh họ hàng cười ngặt nghẽo.',
            voiceover: 'Ba năm làm rể miệt vườn, sáng chèo ghe gom dừa, tối gánh nước tưới cây. Lâm Hoàng Hải cắn răng nhẫn nhục chỉ vì lời hứa che chở cho Thanh Thảo. Nhưng hôm nay, giọt nước đã tràn ly!',
            sound: 'Tiếng xô nhựa đập chát chúa, tiếng cười hô hố của đám người họ hàng miệt vườn, nhạc u ám rền vang.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Thiếu gia vựa cát ép duyên',
            visual: 'Công tử Huỳnh Phú bước xuống từ chiếc xe bán tải bóng loáng, tay kẹp điếu xì gà, ném xấp tiền cọc 20 tỷ và tờ đơn ly hôn thẳng vào ngực Hải.',
            voiceover: 'Huỳnh Phú: "Ký vô đây rồi dọn đồ cút khỏi xứ Bến Tre này! Thằng làm vườn không có cửa đứng chung ghe với Thanh Thảo!". Bà Ba Đạt đon đả rót trà cho thiếu gia cát, ép Hải điểm chỉ ngay.',
            sound: 'Tiếng xấp tiền đập bẹp vào ngực, tiếng nhạc căng thẳng dồn dập.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Xé nát hợp đồng đoạn tuyệt',
            visual: 'Hoàng Hải ngẩng đầu lên. Ánh mắt sắc lạnh như mắt chim ưng săn mồi giữa đồng bằng. Anh cầm tờ đơn ly dị xé toạc làm tư, ném thẳng xuống mâm trầu cau.',
            voiceover: 'Hoàng Hải: "Ly dị? Cái xứ họ Lê các người nghĩ mình cao quý lắm sao? Ba năm qua tôi trả đủ ân nghĩa rồi. Từ giây phút này... đừng hòng nhận một xu cứu mạng từ tôi!".',
            sound: 'Tiếng xé giấy rành rọt (SFX Ripping paper), tiếng thở dốc sững sờ của cả họ.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Huỳnh Phú đe dọa chém rể nghèo',
            visual: 'Huỳnh Phú đập bàn chửi thề, vẫy tay gọi 10 gã giang hồ bến phà rút ống tuýp sắt lao tới vây ráp.',
            voiceover: 'Huỳnh Phú gầm lên: "Thằng ăn bám muốn chết hả mậy? Hôm nay tao dìm xác mày xuống đáy sông Tiền cho cá ăn!". Nhưng Hải chỉ mỉm cười khẩy, bấm nhẹ vào chiếc vòng tay titan đen.',
            sound: 'Tiếng kim loại va chạm leng keng, tiếng hò hét hung hãn, nhạc cao trào kịch tính nghẹt thở.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Quân đoàn sông nước đổ bộ',
            visual: 'Mặt sông Hậu bỗng cuộn sóng dữ dội! 10 chiếc cano quân sự bọc thép xé nước lao tới bến đò, 2 trực thăng vũ trang hạ thấp độ cao làm gió bốc tung toàn bộ rạp cưới họ Lê!',
            voiceover: 'Loa truyền thanh từ trực thăng gầm vang khắp cù lao: "Vùng 4 Hải Đội... Cung nghênh Long Soái Hoàng Hải quy vị!". Mặt Huỳnh Phú và má vợ cắt không còn giọt máu!',
            sound: 'Tiếng cánh quạt trực thăng gầm rú rung chuyển đất trời, tiếng còi cano báo động, tiếng bass drop chấn động ngắt tập!'
          }
        ]
      },
      {
        episodeNumber: 2,
        title: 'Tập 2: Thẻ Đen Vạn Tỷ & Lệnh Phong Sát Mỏ Cát',
        hook: 'Thủ lĩnh hải đoàn bước lên bến đò quỳ một gối trước mặt Hoàng Hải, trao chiếc áo dạ hội đại tướng!',
        cliffhanger: 'Huỳnh Phú nhận cuộc gọi từ tía ruột: "Nghịch tử! Toàn bộ mỏ cát bị rút giấy phép, sà lan chìm hết, nhà mình nợ 1000 tỷ rồi!"',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Nối tiếp tập 1: Tướng quân quỳ lạy',
            visual: 'Tiếp nối ngay khoảnh khắc quân đoàn đổ bộ. Vị đại tá quân hàm rực rỡ bước nhanh lên cầu gỗ, nghiêm trang quỳ một gối trước mặt chàng rể miệt vườn.',
            voiceover: 'Cả họ tộc họ Lê chết lặng giữa sân đình! Người đang quỳ gối xin lệnh tên con rể khố rách lại chính là Tư Lệnh Quân Đoàn mà đến cả chủ tịch tỉnh cũng phải nghênh tiếp từ cổng!',
            sound: 'Tiếng bốt da nện đanh thép trên ván gỗ, tiếng lách cách của súng ống quân dụng.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Má vợ lu loa cãi chày cãi cối',
            visual: 'Bà Ba Đạt lùi lại mấy bước, mồm méo xệch vẫn cố cãi: "Diễn trò! Chắc mày thuê cano của đoàn hát bóng về dọa tao đúng hôn?".',
            voiceover: 'Bà Ba Đạt chỉ tay la làng: "Cậu Phú ơi, kêu công an xã bắt nó đi cậu!". Nhưng Huỳnh Phú lúc này trán vã mồ hôi như tắm, hai đầu gối run lập cập khi nhìn thấy phù hiệu quân đoàn thật.',
            sound: 'Tiếng thở dốc sợ hãi, tiếng xì xào bàn tán bàng hoàng của họ hàng.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Thẻ đen trăm tỷ xuất hiện',
            visual: 'Hoàng Hải rút từ túi áo bà ba ra chiếc thẻ đen Black Card mạ vàng vô hạn mức, ném thẳng vào mâm trầu cau trước mặt Huỳnh Phú.',
            voiceover: 'Hoàng Hải: "20 tỷ của mày to như cái nia vậy sao? Trong thẻ này tao có 100 tỷ tiền mặt. Cả cơ ngơi mỏ cát nhà mày ở xứ Tiền Giang, tao cho 3 phút để xóa sổ!".',
            sound: 'Tiếng thẻ kim loại va vào mâm đồng keng một tiếng sắc lẹm, nhạc dồn dập.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Lệnh phong sát toàn diện đường sông',
            visual: 'Vị Tư Lệnh lập tức rút máy bộ đàm vệ tinh: "Ra lệnh cho Cục Đường Thủy: Đình chỉ mọi giấy phép khai thác của tập đoàn họ Huỳnh, niêm phong toàn bộ 50 sà lan!".',
            voiceover: 'Từng mệnh lệnh ban ra như sét đánh ngang tai. Đám giang hồ của Huỳnh Phú sợ hãi vứt hết gậy sắt nhảy tùm xuống sông bơi thoát thân.',
            sound: 'Tiếng nhảy tùm xuống nước, tiếng radio truyền tin khẩn cấp.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Cuộc gọi phá sản',
            visual: 'Điện thoại của Huỳnh Phú đổ chuông réo rắt. Anh ta bật loa ngoài, đầu dây bên kia tiếng ông trùm mỏ cát gào khóc trong tuyệt vọng.',
            voiceover: 'Tía Huỳnh Phú: "Trời ơi thằng con trời đánh! Mày chọc giận ông lớn nào vậy? Công an kinh tế phong tỏa tài khoản, mỏ cát bị rút phép, nhà mình sạt nghiệp rồi con ơi!". Huỳnh Phú ngã quỵ xuống sình lầy!',
            sound: 'Tiếng gào khóc thảm thiết trong loa thoại, tiếng sét đánh ngang sông, kết thúc tập cực sốc!'
          }
        ]
      },
      {
        episodeNumber: 3,
        title: 'Tập 3 (Đại Kết Cục): Má Vợ Quỳ Lạy & Rước Vợ Lên Du Thuyền',
        hook: 'Má vợ bò lê dưới sân gạch ôm chặt ống quần Hoàng Hải khóc ròng xin tha thứ!',
        cliffhanger: 'Hoàng Hải nắm tay Thanh Thảo bước lên du thuyền triệu đô, để lại cả gia tộc chìm trong biển ân hận khôn nguôi!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Nối tiếp tập 2: Kẻ phản diện sụp đổ',
            visual: 'Huỳnh Phú ôm đầu đập trán xuống đất, mặt mày dính đầy sình lầy. Cả họ tộc Lê gia đứng nép vào góc cột, run rẩy không dám thở mạnh.',
            voiceover: 'Chỉ vỏn vẹn ba phút, một ông trùm khét tiếng miền Tây đã sụp đổ tan tành. Người nhà họ Lê lúc này mới hiểu ra, chàng rể mà họ coi như con ở chính là bậc quân vương hô mưa gọi gió!',
            sound: 'Tiếng gió rít qua rặng dừa nước, tiếng thở dốc nghẹn ngào.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Màn lật mặt trơ trẽn của má vợ',
            visual: 'Bà Ba Đạt đổi sắc mặt 180 độ, bò tới ôm chặt chân Hoàng Hải, hai tay xoa vạt áo anh nịnh nọt.',
            voiceover: 'Bà Ba Đạt mếu máo: "Hải ơi... à không, Đại Tướng gia! Má biết ngay con là rồng trong loài người mà! Hồi nãy má giả bộ thử lòng con coi có thương Thanh Thảo thiệt lòng hôn đó!".',
            sound: 'Nhạc châm biếm sâu cay, tiếng chép miệng ngán ngẩm của họ hàng.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Vả mặt đanh thép của chàng rể',
            visual: 'Hoàng Hải lạnh lùng rút chân lại, ánh mắt không một chút lay động nhìn người đàn bà tham lam độc địa.',
            voiceover: 'Hoàng Hải: "Thử lòng? Lúc má hắt nước rửa chén vô người tôi, lúc má đếm tiền cọc của thằng Phú gả vợ tôi, sao má không nói thử lòng? Mấy mẫu vườn sầu riêng nhà họ Lê nợ ngân hàng, ba năm qua tôi đã âm thầm trả sạch rồi. Từ nay, ân đoạn nghĩa tuyệt!".',
            sound: 'Tiếng trống dồn dập, lời thoại đanh thép như búa gõ lim.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Chỉ giữ lại mối chân tình',
            visual: 'Hoàng Hải bước tới trước mặt Lê Thanh Thảo. Ánh mắt anh tràn ngập sự dịu dàng, đưa bàn tay ấm áp về phía cô gái đang ngấn lệ.',
            voiceover: 'Hoàng Hải: "Thanh Thảo, ba năm qua em chưa từng khinh anh nghèo hèn. Hôm nay, anh đưa em đi hưởng trọn giang sơn này!". Thanh Thảo mỉm cười trong nước mắt, đặt tay vào lòng bàn tay anh.',
            sound: 'Nhạc đờn ca tài tử pha giao hưởng du dương xúc động.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Đại kết cục viên mãn trên sông nước',
            visual: 'Du thuyền 5 sao màu trắng rẽ sóng cập bến, rước hai người lên boong tàu. Phía sau lưng, cả gia tộc cúi đầu rơi lệ van nài trong vô vọng.',
            voiceover: 'Tiếng còi tàu hú vang xé tan màn sương sớm. Chàng rể miền Tây uy trấn bốn phương, đưa người vợ hiền thảo bước lên đỉnh vinh quang hạnh phúc trọn đời!',
            sound: 'Tiếng còi tàu ngân vang hùng tráng, pháo hoa rực rỡ kết thúc trọn bộ series cực kỳ sảng khoái!'
          }
        ]
      }
    ]
  },

  // 2. TRÙNG SINH BÁO THÙ (BỐI CẢNH NHÀ CỔ ĐIỀN CHỦ BẠC LIÊU)
  {
    id: 'trung-sinh-dien-chu-bac-lieu',
    title: 'Trùng Sinh: Con Gái Thật Của Điền Chủ Trở Về',
    originalTitle: 'Gốc TQ: 真假千金: 重生复仇 (Chân Giả Thiên Kim)',
    vietnameseSetting: 'Miền Tây Nam Bộ: Dinh thự nhà cổ Bạc Liêu & Vựa lúa xuất khẩu lớn nhất đồng bằng',
    genre: 'Trùng Sinh Báo Thù',
    genreKey: 'trung_sinh',
    tags: ['Trùng sinh', 'Vả mặt trà xanh', 'Thiên kim thật', 'Điền chủ Bạc Liêu', 'Trọn bộ Full Arc'],
    addictionRating: 4.9,
    viewsEstimate: '2.8 Tỷ lượt xem Douyin/TikTok',
    hook: 'Kiếp trước bị em gái nuôi trà xanh đẩy xuống sông Tiền cướp gia sản, kiếp này trùng sinh đúng ngày giỗ hội đồng công bố di chúc!',
    synopsis: 'Kiếp trước, Đặng Hoàng Yến là con gái ruột duy nhất của đại điền chủ giàu nhất Bạc Liêu nhưng bị cô em nuôi giả tạo Ngọc Bích vu oan lừa gạt, cha mẹ ruột mù quáng ép hiến thận rồi bị đẩy xuống sông Tiền chết đuối trong lạnh lẽo. Trùng sinh về năm 20 tuổi đúng buổi lễ công bố quyền thừa kế vựa lúa, Hoàng Yến mang theo bản giám định ADN của Viện Pháp Y Sài Gòn, vạch trần âm mưu tráo con và liên minh với thiếu gia Trần Tuấn Kiệt - ông trùm cảng biển miền Nam để tống cổ ả trà xanh vào tù!',
    characters: [
      { name: 'Đặng Hoàng Yến', role: 'Nữ chính', description: 'Trùng sinh mang ký ức kiếp trước và tài kinh doanh lúa gạo xuất sắc, quyết tâm đòi lại công lý.' },
      { name: 'Ngọc Bích', role: 'Em nuôi trà xanh', description: 'Bề ngoài yếu đuối giả nai nhưng tâm địa ác độc như rắn rết.' },
      { name: 'Trần Tuấn Kiệt', role: 'Nam chính', description: 'Đại lão trùm cảng biển logistics miền Nam, quyền lực ngút trời nhưng chung tình vô điều kiện.' },
      { name: 'Bà Cả Đặng', role: 'Mẹ ruột u mê', description: 'Mù quáng tin lời con nuôi, sau khi biết sự thật thì hối hận quỳ lạy van xin.' },
    ],
    viralHighlights: [
      'Ném bản ADN và đoạn ghi âm tráo đổi con gái của bà vú nuôi vào máy chiếu hội nghị.',
      'Cái tát nảy lửa tống cổ trà xanh xuống sông Bạc Liêu.',
      'Tuấn Kiệt xuất hiện với đoàn 30 xe bọc thép tuyên bố: "Ai dám đụng tới vợ tôi, chuẩn bị ra biển mà ăn năn!".',
    ],
    suggestedEpisodes: 3,
    isFullSeries: true,
    episodes: [
      {
        episodeNumber: 1,
        title: 'Tập 1: Giọt Máu Trùng Sinh & Tờ Di Chúc Nhuốm Máu',
        hook: 'Mở mắt thấy mình đang đứng giữa sân nhà cổ Bạc Liêu, trên tay trà xanh là bản ADN giả mạo!',
        cliffhanger: 'Màn hình máy chiếu bất ngờ nhảy sang đoạn clip trà xanh bỏ thuốc độc vào ly trà của ông Cả!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Mở màn trùng sinh',
            visual: 'Hình ảnh dòng sông lạnh buốt và nụ cười ghê tởm của Ngọc Bích. Chớp mắt chuyển sang ánh đèn lồng rực rỡ của dinh thự cổ Bạc Liêu.',
            voiceover: 'Cảm giác ngạt thở dưới đáy sông sâu vẫn còn lạnh buốt da thịt. Nhưng khi mở mắt ra, Đặng Hoàng Yến biết mình đã sống lại! Sống lại đúng ngày gia tộc họp mặt chia gia tài vựa lúa!',
            sound: 'Tiếng sóng nước gầm rú chuyển sang tiếng đàn tranh da diết và tiếng bàn tán xôn xao.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Trà xanh diễn kịch khóc lóc',
            visual: 'Ngọc Bích mặc áo bà ba lụa trắng, tay cầm khăn rằn chấm nước mắt, đứng tựa vào bà Cả.',
            voiceover: 'Ngọc Bích nức nở: "Chị Yến từ ruộng đồng về chưa hiểu phép tắc, con xin nhường hết mấy căn nhà phố cho chị... Miễn chị đừng ghét con!". Họ hàng nhìn Hoàng Yến với ánh mắt khinh bỉ.',
            sound: 'Tiếng xì xào mỉa mai: "Thứ con gái quê mùa vừa về đã đòi chiếm hết gia sản!".'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Nụ cười phản đòn của người sống lại',
            visual: 'Hoàng Yến mỉm cười sắc lạnh, rút từ túi xách ra chiếc USB cắm thẳng vào máy chiếu tiệc gia tộc.',
            voiceover: 'Hoàng Yến: "Nhường? Một đứa con gái của bà bảo mẫu tráo đổi năm xưa, mà cũng xứng nói từ nhường sao?". Giọng nói đanh thép vang lên khiến cả dinh thự nín thở.',
            sound: 'Nhạc điện tử dồn dập, tiếng phím bấm click dứt khoát.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Vạch trần ADN thật của Viện Pháp Y',
            visual: 'Màn hình máy chiếu hiện rõ bản giám định gen của Viện Pháp Y TP.HCM: Hoàng Yến trùng khớp 99.99% với ông Cả, còn Ngọc Bích là huyết thống với tài xế!',
            voiceover: 'Hoàng Yến: "Tờ giấy trên tay cô là đồ giả mua 50 triệu ngoài bến xe. Còn đây mới là bản ADN có mộc đỏ nhà nước!". Mặt Ngọc Bích cắt không còn giọt máu.',
            sound: 'Tiếng máy ảnh chớp lia lịa, tiếng quan khách ồ lên kinh hoàng.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Clip độc dược',
            visual: 'Chưa dừng lại, màn hình nhảy sang đoạn video giám sát: Ngọc Bích đang lén rắc thuốc độc vào ấm trà của ông Cả Đặng!',
            voiceover: 'Hoàng Yến gằn từng tiếng: "Má xem đi! Đây là đứa con gái ngoan má ngày đêm yêu chiều đây!". Ngọc Bích hét lên điên cuồng lao tới giật dây điện!',
            sound: 'Tiếng thét kinh hãi của trà xanh, tiếng bà Cả đánh rơi tách trà vỡ tan tành!'
          }
        ]
      },
      {
        episodeNumber: 2,
        title: 'Tập 2: Bạt Tai Đanh Thép & Thiếu Gia Cảng Biển Giáng Lâm',
        hook: 'Bà Cả vung tay tát thẳng vào mặt trà xanh một cái bạt tai nảy lửa khiến ả ngã dúi dụi!',
        cliffhanger: 'Đại lão Tuấn Kiệt dẫn đoàn vệ sĩ áo đen bước vào: "Vợ của tôi, kẻ nào dám đụng vào một sợi tóc?"',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Nối tiếp tập 1: Cái tát thức tỉnh',
            visual: 'Tiếp nối ngay tiếng thét của Ngọc Bích. Bà Cả Đặng run rẩy tiến tới, vung trọn bàn tay tát thẳng vào mặt ả con nuôi tráo trở.',
            voiceover: 'Một cái tát trời giáng xé tan lớp mặt nạ ngoan hiền suốt 20 năm! Đứa con mà bà yêu chiều hết mực lại từng ngày rắp tâm đầu độc cha mẹ để cướp gia sản!',
            sound: 'Tiếng tát cực mạnh nảy lửa (SFX Slap echo), tiếng thở dốc nghẹn ngào.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Trà xanh hóa điên rút dao',
            visual: 'Ngọc Bích bò dậy, tóc tai rũ rượi, ánh mắt độc ác rút chiếc kéo cắt vải trên bàn thờ lao về phía Hoàng Yến.',
            voiceover: 'Ngọc Bích gầm lên: "Bà già ngu dốt! Mày chết đi Hoàng Yến, gia tài này phải là của tao!". Cả sảnh đường la hét hoảng loạn.',
            sound: 'Tiếng kim loại rít lên ghê rợn, tiếng phụ nữ la thất thanh.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Hoàng Yến phản đòn chuẩn xác',
            visual: 'Hoàng Yến không hề lùi bước. Với phản xạ kiếp trước, cô chụp lấy cổ tay Ngọc Bích, bẻ ngược khớp khiến chiếc kéo rơi leng keng, tiện tay bồi thêm một cái tát thứ hai.',
            voiceover: 'Hoàng Yến lạnh lùng: "Kiếp trước tao để mày giết, kiếp này mày không có cửa!". Ngọc Bích ngã quỵ xuống sàn ôm mặt khóc ròng.',
            sound: 'Tiếng kéo rơi leng keng trên nền gạch tàu cổ, tiếng xương khớp rắc nhẹ.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Đám côn đồ bảo kê xuất hiện',
            visual: 'Gã nhân tình của Ngọc Bích - trùm cho vay nặng lãi bến xe dẫn theo 20 tên côn đồ cầm mã tấu xông vào cổng nhà cổ.',
            voiceover: 'Gã giang hồ hống hách: "Ai dám đụng tới Bích của tao? Đất Bạc Liêu này tao là luật! Hôm nay con khốn Hoàng Yến đừng hòng sống sót bước ra!".',
            sound: 'Tiếng mã tấu lê trên thềm xi măng rợn người, nhạc nghẹt thở.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Tuấn Kiệt xuất hiện',
            visual: 'Cánh cổng gỗ lim bật mở tung! 10 chiếc SUV đen tuyền lao vào sân, thiếu gia Trần Tuấn Kiệt mặc âu phục lịch lãm, khí chất vương giả sải bước xuống xe.',
            voiceover: 'Tuấn Kiệt cất giọng trầm ấm mà uy nghiêm: "Đất Bạc Liêu này mày là luật? Còn tao... là luật của cả cái miền Nam này! Kẻ nào dám đụng vào vợ chưa cưới của tao?". Đám côn đồ vứt mã tấu quỳ lạy!',
            sound: 'Tiếng súng lên đạn đồng loạt của vệ sĩ, tiếng rầm rầm áp đảo kết thúc tập!'
          }
        ]
      },
      {
        episodeNumber: 3,
        title: 'Tập 3 (Đại Kết Cục): Tống Cổ Vào Tù & Hôn Lễ Bên Bờ Sông',
        hook: 'Công an kinh tế áp giải Ngọc Bích và đồng bọn lên xe tù trước sự chứng kiến của cả tỉnh!',
        cliffhanger: 'Tuấn Kiệt quỳ một gối trao nhẫn kim cương, sáp nhập hệ thống vựa lúa và cảng biển thành tập đoàn lớn nhất đất nước!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Nối tiếp tập 2: Tra tay vào còng số 8',
            visual: 'Cảnh sát hình sự ập vào bắt giữ toàn bộ băng nhóm cho vay và ả trà xanh Ngọc Bích.',
            voiceover: 'Còng số 8 khóa chặt cổ tay những kẻ bất nhân. Ngọc Bích gào khóc xin tha nhưng tiếng còi xe cảnh sát đã át đi tất cả lời ngụy biện muộn màng.',
            sound: 'Tiếng còng số 8 khóa cạch, tiếng còi xe đặc chủng hú dồn dập.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Cha mẹ ruột ăn năn sám hối',
            visual: 'Ông bà Cả Đặng quỳ sụp trước mặt Hoàng Yến, hai dòng nước mắt chảy dài trên gương mặt già nua.',
            voiceover: 'Bà Cả nức nở: "Yến ơi, cha má mù quáng làm con khổ sở bao năm... Con có hận cha má cũng xin chịu!". Hoàng Yến đỡ cha mẹ dậy, ân oán kiếp trước nay đã chính thức buông bỏ.',
            sound: 'Nhạc vĩ cầm sâu lắng, tiếng thút thít xúc động.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Tiếp quản đế chế lúa gạo',
            visual: 'Hoàng Yến ký vào hợp đồng điều hành 100 vựa lúa xuất khẩu của gia tộc, phong thái đĩnh đạc của nữ doanh nhân tài ba.',
            voiceover: 'Tự tay lấy lại những gì thuộc về mình, Hoàng Yến đưa nông sản miền Tây vươn tầm thế giới, tạo công ăn việc làm cho hàng ngàn bà con nghèo.',
            sound: 'Tiếng vỗ tay hoan hô vang dội của bà con xóm giềng.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Lời cầu hôn ngọt ngào',
            visual: 'Bên bến sông quê thơ mộng ngập tràn ánh hoàng hôn, Tuấn Kiệt nhẹ nhàng rút chiếc nhẫn ngọc bích gia bảo trao cho cô.',
            voiceover: 'Tuấn Kiệt mỉm cười: "Kiếp này có anh ở đây, không ai có thể làm em rơi thêm một giọt nước mắt nào nữa!". Hoàng Yến mỉm cười hạnh phúc gật đầu.',
            sound: 'Nhạc tình yêu du dương ngọt ngào, tiếng sóng vỗ êm đềm.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Đại kết cục viên mãn',
            visual: 'Khung cảnh đám cưới cổ tích miền Tây rực rỡ sắc hoa bên bến đò, đôi lứa hạnh phúc sánh bước trong sự chúc phúc của muôn người.',
            voiceover: 'Trùng sinh báo thù thành công, bảo vệ gia đình và tìm thấy tình yêu đích thực. Một cuộc đời mới rực rỡ và viên mãn khép lại trọn vẹn!',
            sound: 'Tiếng pháo hoa giòn giã, khúc ca mừng vui kết thúc trọn vẹn series!'
          }
        ]
      }
    ]
  },

  // 3. GIẢ NGHÈO ĐI COI MẮT GẶP NỮ CHỦ TỊCH BẾN TRE
  {
    id: 'gia-ngheo-coi-mat-nu-chu-tich',
    title: 'Giả Nghèo Coi Mắt: Cưới Ngay Nữ Chủ Tịch Bến Tre',
    originalTitle: 'Gốc TQ: 装穷相亲: 闪婚万亿女总裁',
    vietnameseSetting: 'Miệt vườn Bến Tre & Quán cà phê sông nước: Vựa dừa và xuất khẩu nông sản Nam Bộ',
    genre: 'Giả Nghèo Coi Mắt',
    genreKey: 'gia_ngheo',
    tags: ['Giả nghèo', 'Coi mắt', 'Xe ôm công nghệ', 'Nữ chủ tịch Bến Tre', 'Hài hước vả mặt', 'Trọn bộ Full Arc'],
    addictionRating: 4.9,
    viewsEstimate: '2.5 Tỷ lượt xem Douyin/TikTok',
    hook: 'Chủ tịch tập đoàn tỷ đô mặc áo xe ôm công nghệ đi coi mắt bị gái thực dụng hắt ly nước mía, nữ chủ tịch vựa dừa bàn bên kéo tay đi làm giấy kết hôn!',
    synopsis: 'Trần Quốc Phong - chủ tịch tập đoàn nông nghiệp tỷ đô, chán ngấy những cô nàng đào mỏ nên mặc áo khoác xe ôm chạy xe máy cà tàng đi coi mắt ở quán cà phê ven sông Tiền. Bị cô gái thực dụng mắng chửi thậm tệ và đòi sính lễ 5 tỷ, đúng lúc này, nữ chủ tịch tập đoàn xuất khẩu nông sản Bến Tre Nguyễn Mai Phương ngồi bàn bên đứng dậy kéo tay anh đi đăng ký kết hôn luôn! Chuỗi ngày vừa giả nghèo vừa cưng chiều vợ yêu và vả mặt từng kẻ coi thường bắt đầu!',
    characters: [
      { name: 'Trần Quốc Phong', role: 'Nam chính', description: 'Chủ tịch tập đoàn nông nghiệp nghìn tỷ ẩn thân làm tài xế xe ôm.' },
      { name: 'Nguyễn Mai Phương', role: 'Nữ chính', description: 'Nữ chủ tịch xinh đẹp, bản lĩnh, cần kết hôn để đối phó gia đình ép gả.' },
      { name: 'Cô gái thực dụng', role: 'Phản diện', description: 'Mê tiền, đòi quà cưới trên trời rồi bị bóc mẽ.' },
      { name: 'Thiếu gia chủ vựa sầu riêng', role: 'Tình địch hách dịch', description: 'Cậy giàu khiêu khích Phong và cái kết đắng.' },
    ],
    viralHighlights: [
      'Gái thực dụng đòi 5 tỷ sính lễ bị vạch mặt đi thuê túi xách hàng chợ.',
      'Nữ chủ tịch kéo xe ôm đi đăng ký kết hôn trong 10 phút.',
      'Cốp xe cà tàng mở ra chìa khóa du thuyền triệu đô và hợp đồng mua lại toàn bộ khu phố thương mại.',
    ],
    suggestedEpisodes: 3,
    isFullSeries: true,
    episodes: [
      {
        episodeNumber: 1,
        title: 'Tập 1: Ly Nước Mía Coi Mắt & Cú Chốt Đăng Ký Kết Hôn',
        hook: 'Cô gái thực dụng cầm ly nước mía tạt thẳng vào áo khoác xe ôm của Quốc Phong!',
        cliffhanger: 'Nữ chủ tịch Mai Phương kéo Phong lên chiếc Maybach: "Tôi bao nuôi anh mỗi tháng 100 triệu, dám cưới tôi không?"',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Coi mắt thảm họa ven sông',
            visual: 'Trong quán cà phê sân vườn ven sông Tiền, cô gái sơn móng tay đỏ chót chĩa thẳng vào mặt Quốc Phong đang mặc áo xe ôm.',
            voiceover: 'Cô gái khinh bỉ: "Một ngày chạy xe kiếm được mấy đồng bạc mà dám hẹn tui ra đây? Không có nhà lầu xe hơi thì về bưng thúng bán xôi đi!".',
            sound: 'Tiếng ly nước mía đập mạnh xuống bàn cọt kẹt, nhạc châm biếm hài hước.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Đòi sính lễ trên trời',
            visual: 'Ả chìa tờ giấy danh sách: "Sính lễ 5 tỷ tiền mặt, sổ đỏ căn biệt thự sang tên tui một mình, hàng tháng nộp hết tiền cho má tui!".',
            voiceover: 'Quốc Phong thong thả húp ngụm trà: "Em đi tìm chồng hay đi tìm cây ATM vậy em gái?". Cô ả tức đỏ mặt, giơ ly nước mía toan tạt thẳng vào mặt anh.',
            sound: 'Tiếng cười khẩy, tiếng gió rít khi tay vung lên.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Bàn tay cứu nguy xuất hiện',
            visual: 'Bàn tay trắng nõn thon thả đeo đồng hồ bạc tỷ bất ngờ giữ chặt tay cô gái thực dụng. Nguyễn Mai Phương khí chất ngời ngời bước ra.',
            voiceover: 'Mai Phương lạnh lùng: "Túi xách hàng chợ đi thuê 200 ngàn một ngày, nợ tiền cà phê tuần trước chưa trả mà dám ở đây lên giọng?". Ả gái xấu hổ che mặt chạy té khói.',
            sound: 'Tiếng giày cao gót nhịp nhàng, tiếng thực khách trầm trồ: "Đó là Mai Phương, nữ tướng xứ dừa!".'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Lời rủ rê chớp nhoáng',
            visual: 'Mai Phương quay sang nhìn anh chàng xe ôm điển trai, mắt ánh lên vẻ tinh nghịch.',
            voiceover: 'Mai Phương: "Anh gì ơi, tôi đang bị gia đình ép lấy gã thiếu gia cờ bạc. Anh có căn cước công dân ở đây không? Cùng tôi ra phường đăng ký kết hôn luôn!". Quốc Phong tròn mắt ngạc nhiên.',
            sound: 'Nhạc tình yêu pha chút hồi hộp dí dỏm.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Chiếc Maybach nổ máy',
            visual: 'Mai Phương kéo tay Phong lên chiếc Maybach đỗ ven đường, tài xế mở cửa cúi chào. Chiếc xe lướt đi trong ánh mắt kinh hoàng của mọi người.',
            voiceover: 'Từ một tài xế xe ôm bị chê bần hàn, bỗng chốc trở thành chồng hợp pháp của nữ chủ tịch giàu nhất xứ Bến Tre! Sóng gió hào môn chính thức bắt đầu!',
            sound: 'Tiếng động cơ xe sang gầm vang, tiếng drumroll kết tập cực cuốn!'
          }
        ]
      },
      {
        episodeNumber: 2,
        title: 'Tập 2: Ra Mắt Gia Tộc Miệt Vườn & Thiếu Gia Khiêu Khích',
        hook: 'Thiếu gia chủ vựa sầu riêng ném chìa khóa xe Porsche trúng chân Quốc Phong: "Thằng xe ôm cút đi!"',
        cliffhanger: 'Thiếu gia gọi điện cho Tổng Giám Đốc tập đoàn nông nghiệp đòi cấm cửa Phong, nhưng đầu dây bên kia bảo Chủ Tịch đang đứng trước mặt!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Nối tiếp tập 1: Bước vào biệt phủ gia tộc',
            visual: 'Tiếp nối khoảnh khắc đăng ký kết hôn. Quốc Phong trong bộ đồ giản dị đi cạnh Mai Phương bước vào dinh thự nhà họ Nguyễn, họ hàng bĩu môi khinh khỉnh.',
            voiceover: 'Vừa cầm tờ giấy kết hôn còn thơm mùi mực, Mai Phương dắt ngay người chồng mới cưới về ra mắt để dập tắt âm mưu ép gả. Một trận cuồng phong lập tức nổ ra!',
            sound: 'Tiếng bước chân trong sảnh lát gạch hoa cổ, tiếng xì xào mỉa mai.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Tình địch cậy tiền ép bức',
            visual: 'Thiếu gia vựa sầu riêng ngậm tăm, xoay chiếc chìa khóa xe sang: "Mai Phương, em chê anh mà đi rước một thằng chạy xe ôm về làm chồng sao?".',
            voiceover: 'Hắn ném tờ séc 10 tỷ xuống chân Phong: "Cầm tiền này về quê mua chiếc ghe mới, rồi biến khỏi tầm mắt của tao!". Mai Phương tức giận định bước lên thì Phong khẽ giữ tay vợ lại.',
            sound: 'Tiếng giấy séc rơi lảo đảo, tiếng cười khả ố của đám bạn nhậu.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Sự kiên định của nữ chủ tịch',
            visual: 'Mai Phương gạt phăng tờ séc, khoác chặt tay Quốc Phong: "Anh ấy là chồng tui! Dù anh ấy có chạy xe ôm, tui cũng nuôi được anh ấy cả đời!".',
            voiceover: 'Lời nói dứt khoát của người vợ mới cưới khiến Quốc Phong cảm động sâu sắc. Người phụ nữ này, anh nhất định phải dùng cả cơ nghiệp tỷ đô để bảo bọc!',
            sound: 'Nhạc nền ấm áp xúc động nhưng tràn đầy quyết tâm.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Thiếu gia gọi điện uy hiếp',
            visual: 'Thiếu gia rút điện thoại gọi cho đối tác lớn nhất chuỗi cung ứng lúa gạo miền Nam nhằm khoe khoang quyền lực.',
            voiceover: 'Hắn quát vào máy: "Alo anh Ba! Có thằng xe ôm tên Trần Quốc Phong dám cản mũi em, anh cắt hết hàng hóa không cho nó chạy xe nữa nhé!".',
            sound: 'Tiếng chuông điện thoại tút tút căng thẳng.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Cao trào & Cliffhanger - Cuộc gọi phản đòn',
            visual: 'Đầu dây bên kia tiếng ông trùm cung ứng run lẩy bẩy quát ngược lại.',
            voiceover: 'Đầu dây bên kia: "Mày khùng hả? Trần Quốc Phong là Chủ Tịch Sáng Lập kiêm Cổ Đông Nắm 80% tập đoàn tao! Mày muốn nhà mày bị phong sát phá sản hay sao?". Thiếu gia đứng sững như chết lâm sàng!',
            sound: 'Tiếng quát tháo sấm sét trong điện thoại, tiếng bass drop ngắt tập nghẹt thở!'
          }
        ]
      },
      {
        episodeNumber: 3,
        title: 'Tập 3 (Đại Kết Cục): Lộ Thân Phận Chủ Tịch & Đám Cưới Thế Kỷ',
        hook: 'Đoàn 30 siêu xe Rolls-Royce cùng ban lãnh đạo tập đoàn đổ bộ về Bến Tre cúi chào Chủ Tịch!',
        cliffhanger: 'Mai Phương ngỡ ngàng khi biết anh chồng xe ôm mình nhận nuôi thực chất là ông trùm sở hữu cả hệ thống xuất khẩu nông sản châu Á!',
        durationSeconds: 120,
        scenes: [
          {
            sceneNumber: 1,
            timecode: '0:00 - 0:15',
            title: 'Nối tiếp tập 2: Đoàn xe vạn tỷ đổ bộ',
            visual: 'Tiếp nối khoảnh khắc thiếu gia hóa đá. Cổng lớn mở toang, 30 chiếc siêu xe sang trọng nối đuôi nhau xếp kín cả con lộ làng, dàn giám đốc vest đen bước xuống đồng loạt cúi đầu chào Phong.',
            voiceover: 'Ban giám đốc đồng thanh: "Kính chào Trần Chủ Tịch! Toàn bộ hợp đồng sáp nhập chuỗi xuất khẩu nông sản của nhà họ Nguyễn đã hoàn tất theo lệnh ngài!".',
            sound: 'Tiếng cửa xe đóng rầm rập, tiếng hô vang dội cả xóm làng.'
          },
          {
            sceneNumber: 2,
            timecode: '0:15 - 0:45',
            title: 'Thiếu gia quỳ lạy xin tha',
            visual: 'Thiếu gia vựa sầu riêng ngã quỵ xuống đất, bò lết tới ôm chân Quốc Phong tạ tội.',
            voiceover: 'Hắn run rẩy: "Chủ tịch tha cho em, em có mắt như mù!". Quốc Phong lạnh lùng: "Kẻ đem tiền ra chà đạp danh dự người khác thì không xứng làm ăn trên đất này!".',
            sound: 'Tiếng khóc lóc thảm thiết, họ hàng ngơ ngác sợ hãi.'
          },
          {
            sceneNumber: 3,
            timecode: '0:45 - 1:15',
            title: 'Món quà trăm tỷ tặng vợ yêu',
            visual: 'Quốc Phong quay sang nắm tay Mai Phương, ký chuyển nhượng toàn bộ 1000 tỷ cổ phần tập đoàn cho cô đứng tên một mình.',
            voiceover: 'Quốc Phong mỉm cười: "Em từng nói sẽ bao nuôi anh cả đời. Hôm nay anh xin tặng lại em cả đế chế này, để anh làm người đàn ông đứng sau cưng chiều em!".',
            sound: 'Tiếng trầm trồ kinh ngạc của toàn thể gia tộc.'
          },
          {
            sceneNumber: 4,
            timecode: '1:15 - 1:45',
            title: 'Hôn lễ thế kỷ trên cồn dừa',
            visual: 'Lễ cưới cổ tích lộng lẫy được tổ chức ngay giữa miệt vườn rực rỡ hoa trái và ánh đèn lung linh bên sông Tiền.',
            voiceover: 'Từ một cuộc gặp gỡ định mệnh nơi quán cà phê nghèo đến mối duyên vàng trăm năm viên mãn. Cả vùng sông nước tưng bừng chúc phúc cho đôi uyên ương!',
            sound: 'Tiếng pháo nổ rộn rã, tiếng hò reo vui vẻ của bà con.'
          },
          {
            sceneNumber: 5,
            timecode: '1:45 - 2:00',
            title: 'Đại kết cục viên mãn',
            visual: 'Hai người trao nhau nụ hôn ngọt ngào dưới bầu trời rực sáng pháo hoa đêm bên dòng sông quê hiền hòa.',
            voiceover: 'Giả nghèo thử lòng đổi lấy chân tâm, hạnh phúc trọn đời bên người phụ nữ xứng đáng nhất. Khép lại trọn bộ series đầy ắp nụ cười và thỏa mãn!',
            sound: 'Khúc ca hạnh phúc ngân vang dạt dào, kết thúc hoàn mỹ toàn bộ series!'
          }
        ]
      }
    ]
  }
];
