// "social media/one-cikanlar" kapak (1080×1080) ve hikâye (1080×1920) görsellerini üretir.
// Düzen: kapaklar marka mavisi zemin üzerinde beyaz çizgi ikon (Instagram daireye kırpar, ad kapağa yazılmaz);
// hikâyeler üstte fotoğraf, altta açık panelde hashtag, kalın başlık, üç madde ve iletişim satırı.
// Instagram'ın üst (profil) ve alt (yanıt) çubuklarına içerik koyulmaz: önemli öğeler 150–1720 px arasındadır.
// Adlar ve açıklamalar "social media/one-cikanlar/icerik.json" içindedir; bu betik yalnızca görselleri yazar.
//
// Çalıştırma (repo kökünden):
//   docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp -e REPO=/repo -v "$PWD":/repo -w /tmp mcr.microsoft.com/playwright:v1.56.1-noble \
//     bash -c 'npm i -s playwright@1.56.1 >/dev/null 2>&1 && cp /repo/scripts/sosyal-medya/one-cikanlar.mjs . && node one-cikanlar.mjs'
// Yalnızca bazıları için sona dosya önekini ekleyin: ... node one-cikanlar.mjs 03-cam-balkon

import { chromium } from "playwright";
import { writeFileSync } from "node:fs";
import os from "node:os";
import { pathToFileURL } from "node:url";
import path from "node:path";

const root = process.env.REPO ?? path.resolve(import.meta.dirname, "../.."); // betik kopyalanarak çalıştırılır
const out = path.join(root, "social media", "one-cikanlar");
const site = (name) => pathToFileURL(path.join(root, "wwwroot/images", name)).href;
const font = (name) => pathToFileURL(path.join(root, "wwwroot/fonts", name)).href;

const web = "karakusaluminyum.com";
const phones = ["+90 553 835 48 72", "+90 532 510 69 12"];

// tag: hikâyedeki etiket (icerik.json'daki adla aynı). title: satırlar, **kalın** vurgu. points: [ikon, metin].
const items = [
  { file: "01-hakkimizda", tag: "#Hakkımızda", icon: "badge", image: "yana-kayar-surme-cam-sistemleri.webp", focus: "60% 50%",
    title: ["**2001'den beri**", "İzmir'de."],
    points: [["home", "Konut, ticari ve endüstriyel yapılar"], ["window", "Doğrama, cam ve kapı sistemleri"], ["ruler", "Ölçü, teklif ve montaj"]] },
  { file: "02-dograma", tag: "#Doğrama", icon: "window", image: "kapi-ve-pencere-sistemleri.webp", focus: "50% 50%",
    title: ["Alüminyum ve", "**PVC doğrama.**"],
    points: [["home", "Konut ve ticari yapılar için"], ["ruler", "Geniş açıklıklardan standart pencerelere"], ["layers", "Projeye uygun profil ve cam seçimi"]] },
  { file: "03-cam-balkon", tag: "#CamBalkon", icon: "balcony", image: "cam-balkon.webp", focus: "55% 50%",
    title: ["Balkonunuz", "**dört mevsim**", "kullanımda."],
    points: [["fold", "Katlanır ve sürme seçenekler"], ["ruler", "Balkon ölçüsüne özel üretim"], ["drop", "Rüzgâra ve yağmura karşı koruma"]] },
  { file: "04-giyotin-cam", tag: "#GiyotinCam", icon: "guillotine", image: "otomatik-giyotin-camlama-sistemleri.webp", focus: "60% 50%",
    title: ["Tek dokunuşla", "**açılan teras.**"],
    points: [["motor", "Motorlu ve manuel seçenekler"], ["cup", "Teras, kafe ve villa uygulamaları"], ["view", "Kesintisiz manzara"]] },
  { file: "05-tente-panjur", tag: "#TentePanjur", icon: "awning", image: "otomatik-tente.webp", focus: "50% 50%",
    title: ["Gölge ve", "**güvenlik**", "bir arada."],
    points: [["sun", "Otomatik tente sistemleri"], ["shutter", "Sarmal kepenk ve panjur"], ["motor", "Motorlu kullanım seçenekleri"]] },
  { file: "06-kapi-sistemleri", tag: "#Kapılar", icon: "door", image: "otomatik-kapi-ve-diger-sistemler.webp", focus: "62% 50%",
    title: ["Her girişe", "**doğru kapı.**"],
    points: [["sensor", "Fotoselli ve döner kapılar"], ["shutter", "Endüstriyel ve garaj kapıları"], ["gate", "Bahçe kapısı, bariyer ve rampa"]] },
  { file: "07-ofis-bolme", tag: "#OfisBölme", icon: "partition", image: "ofis-bolme-sistemleri.webp", focus: "50% 50%",
    title: ["Işığı kesmeden", "**alanı ayırın.**"],
    points: [["partition", "Cam ofis bölme sistemleri"], ["office", "Toplantı odası ve yönetici ofisi"], ["check", "Temiz ve hızlı montaj"]] },
  { file: "08-projeler", tag: "#Projeler", icon: "city", image: "giydirme-cephe-sistemleri.webp", focus: "55% 50%",
    title: ["Ölçüden", "**uygulamaya.**"],
    points: [["home", "Konut, site ve villa projeleri"], ["factory", "Ticari ve endüstriyel yapılar"], ["camera", "Tamamlanan işlerden kareler"]] },
  { file: "09-teklif-al", tag: "#TeklifAl", icon: "chat", image: "aluminyum-dograma.webp", focus: "30% 50%",
    title: ["Ölçü ve teklif", "**için ulaşın.**"], contact: true,
    points: [["phone", phones[0]], ["phone", phones[1]], ["pin", "Karacaoğlan Mah. 6252 Sk. No: 3, Bornova/İzmir"]] },
];

// 24×24 çizgi ikonlar; kalınlık kullanıldığı yerde verilir.
const icon = {
  badge: '<circle cx="12" cy="9" r="6"/><path d="m9.5 9 1.7 1.7L14.5 7.4M8.5 13.8 7 22l5-3 5 3-1.5-8.2"/>',
  building: '<path d="M4 21V5l8-3v19M12 8h8v13M2 21h20M7 8h2M7 12h2M7 16h2M15 12h2M15 16h2"/>',
  window: '<rect x="4" y="3" width="16" height="18" rx="1"/><path d="M12 3v18M4 12h16"/>',
  balcony: '<path d="M3 4h18M5 4v9M9.5 4v9M14.5 4v9M19 4v9M3 13h18M3 13v8M21 13v8M3 17h18M7.5 13v8M12 13v8M16.5 13v8"/>',
  guillotine: '<rect x="4" y="3" width="16" height="18" rx="1"/><path d="M4 12h16M12 9V5.5M9.5 8 12 5.5 14.5 8"/>',
  awning: '<path d="M3 5h18l-2 7H5zM3 5v16M5 12q1.75 2 3.5 0 1.75 2 3.5 0 1.75 2 3.5 0 1.75 2 3.5 0"/>',
  door: '<path d="M5 21V3h14v18M2 21h20"/><circle cx="15" cy="12" r="1"/>',
  partition: '<path d="M3 4h18M3 20h18M4 4v16M10 4v16M14 4v16M20 4v16M6.5 9l2-2M16.5 9l2-2"/>',
  city: '<path d="M3 21V10l5-3v14M8 21V4h8v17M16 21v-9h5v9M1 21h22M11 8h2M11 12h2M11 16h2"/>',
  chat: '<path d="M4 4h16v12H9l-5 4z"/><path d="M8 9h8M8 12h5"/>',
  home: '<path d="M3 11 12 4l9 7M5 10v10h14V10"/>',
  ruler: '<path d="M3 17 17 3l4 4L7 21z"/><path d="m7 13 2 2m1-5 2 2m1-5 2 2"/>',
  layers: '<path d="m12 3 9 5-9 5-9-5z"/><path d="m3 13 9 5 9-5"/>',
  fold: '<path d="M4 4v16l5-3V7zM9 7l6-3v16l-6-3M15 4l5 3v10l-5 3"/>',
  drop: '<path d="M12 3s6 7 6 11a6 6 0 0 1-12 0c0-4 6-11 6-11z"/>',
  motor: '<circle cx="12" cy="12" r="8"/><path d="M12 7v5l3 2"/>',
  cup: '<path d="M5 8h11v6a5 5 0 0 1-5 5h-1a5 5 0 0 1-5-5zM16 10h2a2 2 0 0 1 0 4h-2M8 3v2M11 3v2"/>',
  view: '<path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
  sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/>',
  shutter: '<path d="M4 3h16v18H4zM4 7h16M4 11h16M4 15h16"/>',
  sensor: '<path d="M4 21V7h16v14M12 7v14M8 3.5a6 6 0 0 1 8 0"/>',
  gate: '<path d="M3 21V6M21 21V6M3 9h18M3 15h18M7 9v6M11 9v6M15 9v6M19 9v6"/>',
  office: '<path d="M5 21V3h14v18M9 7h2m2 0h2m-6 4h2m2 0h2m-6 4h2m2 0h2"/>',
  check: '<circle cx="12" cy="12" r="9"/><path d="m8 12 3 3 5-6"/>',
  factory: '<path d="M3 21V10l6 4v-4l6 4V5h4v16z"/>',
  camera: '<path d="M3 8h4l2-3h6l2 3h4v12H3z"/><circle cx="12" cy="13" r="4"/>',
  phone: '<path d="M5 3h4l2 5-2.5 1.5a11 11 0 0 0 6 6L16 13l5 2v4a2 2 0 0 1-2 2A16 16 0 0 1 3 5a2 2 0 0 1 2-2z"/>',
  pin: '<path d="M12 21s-7-6.5-7-12a7 7 0 0 1 14 0c0 5.5-7 12-7 12z"/><circle cx="12" cy="9" r="2.5"/>',
};

const esc = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;");
const line = (s) => esc(s).replace(/\*\*(.+?)\*\*/g, "<b>$1</b>");
const svg = (k, cls = "") => `<svg class="${cls}" viewBox="0 0 24 24">${icon[k]}</svg>`;

const base = `
@font-face{font-family:M;src:url("${font("manrope-latin.woff2")}") format("woff2");font-weight:200 800;unicode-range:U+0000-00FF,U+0131,U+2000-206F}
@font-face{font-family:M;src:url("${font("manrope-latin-ext.woff2")}") format("woff2");font-weight:200 800;unicode-range:U+0100-02BA,U+1E00-1EFF}
*{margin:0;padding:0;box-sizing:border-box}
body{position:relative;overflow:hidden;font-family:M,sans-serif}
svg{fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round}`;

function cover(item) {
  // Instagram kapağı ~%85 çapında daireye kırpar; ikon ortadaki güvenli alanda kalır.
  return `<!doctype html><html lang="tr"><head><meta charset="utf-8"><style>${base}
body{width:1080px;height:1080px;color:#fff;background:radial-gradient(circle at 34% 28%,#4f95bf 0%,#3b7ea6 38%,#245676 100%)}
.ring{position:absolute;inset:150px;border:3px solid rgba(255,255,255,.28);border-radius:50%}
.icon{position:absolute;left:50%;top:50%;width:400px;height:400px;transform:translate(-50%,-50%);stroke-width:1.35;filter:drop-shadow(0 6px 18px rgba(8,30,46,.35))}
</style></head><body><div class="ring"></div>${svg(item.icon, "icon")}</body></html>`;
}

function story(item) {
  const points = item.points.map(([k, t]) => `<li><span>${svg(k)}</span>${esc(t)}</li>`).join("");
  const foot = item.contact
    ? `<div class="foot"><b>${web}</b><span>Instagram'dan mesaj atabilirsiniz</span></div>`
    : `<div class="foot"><b>${web}</b><span>Teklif için ${phones[0]}</span></div>`;
  return `<!doctype html><html lang="tr"><head><meta charset="utf-8"><style>${base}
body{width:1080px;height:1920px;color:#0d1115;background:#f3f5f6}
.photo{position:absolute;left:0;top:0;width:100%;height:1020px;object-fit:cover;object-position:${item.focus}}
.shade{position:absolute;left:0;top:0;width:100%;height:1020px;background:linear-gradient(180deg,rgba(8,11,14,.62) 0%,rgba(8,11,14,0) 26%,rgba(8,11,14,0) 70%,rgba(8,11,14,.35) 100%)}
.logo{position:absolute;left:80px;top:160px;width:290px;filter:drop-shadow(0 2px 12px rgba(0,0,0,.4))}
.edge{position:absolute;left:0;top:1020px;width:100%;height:10px;background:linear-gradient(115deg,#f2f4f5 0%,#b4bcc1 42%,#e9ecee 68%,#a5aeb4 100%)}
.tag{position:absolute;left:80px;top:934px;padding:16px 30px;color:#fff;background:#3b7ea6;font-size:38px;font-weight:800;letter-spacing:.01em;box-shadow:0 10px 30px rgba(8,30,46,.3)}
.panel{position:absolute;left:80px;right:80px;top:1100px}
h1{font-size:${item.title.length > 2 ? 74 : 84}px;line-height:1.06;font-weight:300;letter-spacing:-.025em}
h1 b{font-weight:800;color:#2a5f80}
ul{list-style:none;margin-top:48px;display:grid;gap:26px}
li{display:flex;align-items:center;gap:24px;font-size:${item.contact ? 31 : 33}px;font-weight:600;line-height:1.25;color:#272d33}
li span{flex:0 0 auto;display:grid;place-items:center;width:76px;height:76px;color:#fff;background:#0d1115}
li svg{width:40px;height:40px;stroke-width:1.7}
.foot{position:absolute;left:80px;right:80px;top:1700px;display:flex;justify-content:space-between;align-items:center;padding-top:26px;border-top:2px solid #c6ccd0;font-size:27px;color:#626c74}
.foot b{color:#0d1115;font-weight:800}
</style></head><body>
<img class="photo" src="${site(item.image)}"><div class="shade"></div><img class="logo" src="${site("logo-ust.webp")}">
<div class="edge"></div><div class="tag">${esc(item.tag)}</div>
<div class="panel"><h1>${item.title.map(line).join("<br>")}</h1><ul>${points}</ul></div>${foot}
</body></html>`;
}

const only = process.argv.slice(2);
const browser = await chromium.launch();
const page = await browser.newPage();
const temp = path.join(os.tmpdir(), "one-cikanlar.html");
for (const item of items.filter((x) => only.length === 0 || only.some((o) => x.file.startsWith(o)))) {
  for (const [kind, render, size] of [["kapak", cover, [1080, 1080]], ["hikaye", story, [1080, 1920]]]) {
    // Yerel fotoğraf ve yazı tipleri yalnızca file:// adresinden açılan sayfaya yüklenir.
    writeFileSync(temp, render(item));
    await page.setViewportSize({ width: size[0], height: size[1] });
    await page.goto(pathToFileURL(temp).href, { waitUntil: "load" });
    await page.evaluate(() => document.fonts.ready);
    const target = path.join(out, `${item.file}-${kind}.jpg`);
    await page.screenshot({ path: target, type: "jpeg", quality: 90 });
    console.log("yazıldı:", path.relative(root, target));
  }
}
await browser.close();
