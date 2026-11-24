/**
 * Suggested countryside / nostalgia tracks on Pixabay Music (free for commercial use, no credit needed), each checked
 * on 2026-11-24 to NOT be "Content ID Registered" — registered tracks can get a Facebook copyright claim. A file
 * downloaded from Pixabay keeps the track number at the end of its name ("…-165947.mp3"), so the music store can
 * recognise it and fill in a Vietnamese name and the mood the AI matches against the script.
 */
export interface MusicPreset {
  id: string;
  name: string;
  mood: string;
  url: string;
  seconds: number;
}

export const MUSIC_PRESETS: MusicPreset[] = [
  { id: '165947', name: 'Đàn dây buồn nhẹ', mood: 'buồn nhẹ, hoài niệm, day dứt, đàn dây', seconds: 251,
    url: 'https://pixabay.com/music/world-gentle-sadness-stringed-plucked-instrument-nostalgia-165947/' },
  { id: '575883', name: 'Piano mưa nghẹn ngào', mood: 'nghẹn ngào, nước mắt, mưa, piano, buồn', seconds: 199,
    url: 'https://pixabay.com/music/modern-classical-sad-piano-emotional-rain-story-575883/' },
  { id: '488449', name: 'Piano sáo kể chuyện làng quê', mood: 'hoài niệm, làng quê, kể chuyện, ấm áp, quê hương', seconds: 164,
    url: 'https://pixabay.com/music/folk-emotional-folk-piano-and-flute-for-nostalgic-village-storytelling-488449/' },
  { id: '379956', name: 'Guitar thở dài', mood: 'da diết, nhớ cha mẹ, hoài niệm, guitar mộc', seconds: 240,
    url: 'https://pixabay.com/music/acoustic-group-guitar-sighs-379956/' },
  { id: '534837', name: 'Guitar dòng sông lặng', mood: 'ấm áp, tình cảm, nhẹ nhàng, guitar', seconds: 152,
    url: 'https://pixabay.com/music/latin-silent-river-intimate-bolero-guitar-instrumental-534837/' },
  { id: '339826', name: 'Sáo trúc sông quê', mood: 'thương quê, sông nước, quê hương, sáo trúc', seconds: 240,
    url: 'https://pixabay.com/music/world-soulful-river-folk-tune-with-bamboo-flute-339826/' },
  { id: '618302', name: 'Sáo trúc đàn tranh yên bình', mood: 'yên bình, đồng quê, nhẹ nhàng, sáo trúc, đàn tranh', seconds: 203,
    url: 'https://pixabay.com/music/china-chinese-bamboo-flute-amp-guzheng-relaxing-zen-music-618302/' },
  { id: '613938', name: 'Sáo trúc chậm thanh bình', mood: 'yên bình, chậm, thanh tịnh, sáo trúc', seconds: 247,
    url: 'https://pixabay.com/music/meditationspiritual-chinese-bamboo-flute-peaceful-zen-music-613938/' },
  { id: '410476', name: 'Suối núi sáo đàn', mood: 'nhẹ nhàng, thanh bình, quê hương, sáo, đàn tranh', seconds: 266,
    url: 'https://pixabay.com/music/world-mountain-spring-full-version-chinese-music-410476/' },
  { id: '359055', name: 'Sáng quê sáo và chim hót', mood: 'đồng quê buổi sáng, chim hót, yên bình, quê hương', seconds: 240,
    url: 'https://pixabay.com/music/world-traditional-countryside-vibe-with-flute-tabla-and-morning-birds-359055/' },
  { id: '578506', name: 'Sáo đồng cỏ tươi sáng', mood: 'tươi sáng, hy vọng, tuổi thơ, vui tươi', seconds: 200,
    url: 'https://pixabay.com/music/scotland-flute-meadow-breeze-melody-578506/' },
];

/** The suggestion a downloaded file belongs to, from the Pixabay track number in its name. */
export function presetForFile(fileName: string): MusicPreset | undefined {
  const base = fileName.replace(/\.[^.]+$/, '');
  return MUSIC_PRESETS.find((p) => new RegExp(`(^|\\D)${p.id}(\\D|$)`).test(base));
}
