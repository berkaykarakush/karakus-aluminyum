// "social media/one-cikanlar" kapak (1080×1080) ve hikâye (1080×1920) görsellerini üretir.
// Düzen: kapaklar marka mavisi zemin üzerinde beyaz çizgi ikon (Instagram daireye kırpar, ad kapağa yazılmaz);
// hikâyeler tam boy fotoğrafın koyu zemine karardığı düzende etiket, serif başlık, üç madde ve site adresi.
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

// label: hikâyedeki üst etiket. title: satırlar, **…** italik vurgu. points: kısa çizgili maddeler. web: false ise site satırı yazılmaz.
const items = [
  { file: "01-hakkimizda", label: "Hakkımızda", icon: "badge", image: "yana-kayar-surme-cam-sistemleri.webp", focus: "20% 50%",
    title: ["2001’den beri", "**İzmir’de.**"],
    points: ["Konut, ticari ve endüstriyel yapılar", "Alüminyum ve PVC doğrama, cam ve kapı sistemleri", "Bornova, İzmir"] },
  { file: "02-dograma", label: "Doğrama", icon: "window", image: "kapi-ve-pencere-sistemleri.webp", focus: "50% 50%",
    title: ["Alüminyum ve", "**PVC doğrama.**"],
    points: ["Konut ve ticari yapılar için", "Geniş açıklıklardan standart pencerelere", "Projeye uygun profil ve cam seçimi"] },
  { file: "03-cam-balkon", label: "Cam Balkon", icon: "balcony", image: "katlanir-cam-sistemleri.webp", focus: "45% 50%",
    title: ["Katlanır ve", "**sürme cam balkon.**"],
    points: ["Rüzgâr, yağmur ve tozu keser", "Manzarayı kapatmaz", "Balkonunuza uygun sistemi birlikte seçelim"] },
  { file: "04-giyotin-cam", label: "Giyotin Cam", icon: "guillotine", image: "otomatik-giyotin-camlama-sistemleri.webp", focus: "50% 50%",
    title: ["Otomatik ve", "**manuel giyotin.**"],
    points: ["Teras, kafe, restoran ve villalar için", "Dikey hareketli cam paneller", "Açıkken manzarayı engellemez"] },
  { file: "05-tente-panjur", label: "Tente & Panjur", icon: "awning", image: "otomatik-tente.webp", focus: "50% 50%",
    title: ["Tente, kepenk", "**ve panjur.**"],
    points: ["Dış mekân gölgelendirmesi", "Kapı ve pencere açıklıkları için kepenk ve panjur", "Alanınıza uygun planlama"] },
  { file: "06-kapi-sistemleri", label: "Kapı Sistemleri", icon: "door", image: "endustriyel-ve-garaj-kapi-sistemleri.webp", focus: "40% 50%",
    title: ["Girişten sevkiyata", "**kapı sistemleri.**"],
    points: ["Fotoselli ve döner kapılar", "Endüstriyel ve garaj kapıları", "Bahçe kapısı, bariyer, rampa ve körük"] },
  { file: "07-ofis-bolme", label: "Ofis Bölme", icon: "partition", image: "ofis-bolme-sistemleri.webp", focus: "50% 50%",
    title: ["Ofisinize uygun", "**cam bölme.**"],
    points: ["Toplantı odası ve yönetici ofisi", "Gün ışığını koruyan cam yüzeyler", "Cam ve dolu panel kombinasyonları"] },
  { file: "08-projeler", label: "Projeler", icon: "city", image: "cam-balkon.webp", focus: "45% 50%", web: false,
    title: ["Uygulamalarımızı", "**inceleyin.**"],
    points: ["Konut, ticari ve endüstriyel projeler", "Tüm projeler web sitemizde", `${web}/projeler`] },
  { file: "09-teklif-al", label: "Teklif Al", icon: "chat", image: "aluminyum-dograma.webp", focus: "15% 50%", web: false,
    title: ["Ölçü ve teklif", "**için ulaşın.**"],
    points: [...phones, "Karacaoğlan Mah. 6252 Sk. No: 3, Bornova/İzmir"] },
];

// Kapak ikonları (24×24 çizgi); kalınlık kullanıldığı yerde verilir.
const icon = {
  badge: '<circle cx="12" cy="9" r="6"/><path d="m9.5 9 1.7 1.7L14.5 7.4M8.5 13.8 7 22l5-3 5 3-1.5-8.2"/>',
  window: '<rect x="4" y="3" width="16" height="18" rx="1"/><path d="M12 3v18M4 12h16"/>',
  balcony: '<path d="M3 4h18M5 4v9M9.5 4v9M14.5 4v9M19 4v9M3 13h18M3 13v8M21 13v8M3 17h18M7.5 13v8M12 13v8M16.5 13v8"/>',
  guillotine: '<rect x="4" y="3" width="16" height="18" rx="1"/><path d="M4 12h16M12 9V5.5M9.5 8 12 5.5 14.5 8"/>',
  awning: '<path d="M3 5h18l-2 7H5zM3 5v16M5 12q1.75 2 3.5 0 1.75 2 3.5 0 1.75 2 3.5 0 1.75 2 3.5 0"/>',
  door: '<path d="M5 21V3h14v18M2 21h20"/><circle cx="15" cy="12" r="1"/>',
  partition: '<path d="M3 4h18M3 20h18M4 4v16M10 4v16M14 4v16M20 4v16M6.5 9l2-2M16.5 9l2-2"/>',
  city: '<path d="M3 21V10l5-3v14M8 21V4h8v17M16 21v-9h5v9M1 21h22M11 8h2M11 12h2M11 16h2"/>',
  chat: '<path d="M4 4h16v12H9l-5 4z"/><path d="M8 9h8M8 12h5"/>',
};

const esc = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;");
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
  // Tam boy fotoğraf aşağıda koyu zemine kararır; etiket, serif başlık, çizgili maddeler ve site adresi.
  const points = item.points.map((t) => `<li>${esc(t)}</li>`).join("");
  return `<!doctype html><html lang="tr"><head><meta charset="utf-8"><style>${base}
@font-face{font-family:S;src:url("${font("instrument-serif-latin.woff2")}") format("woff2");unicode-range:U+0000-00FF,U+0131,U+2000-206F}
@font-face{font-family:S;src:url("${font("instrument-serif-latin-ext.woff2")}") format("woff2");unicode-range:U+0100-02BA,U+1E00-1EFF}
@font-face{font-family:S;font-style:italic;src:url("${font("instrument-serif-italic-latin.woff2")}") format("woff2");unicode-range:U+0000-00FF,U+0131,U+2000-206F}
@font-face{font-family:S;font-style:italic;src:url("${font("instrument-serif-italic-latin-ext.woff2")}") format("woff2");unicode-range:U+0100-02BA,U+1E00-1EFF}
body{width:1080px;height:1920px;color:#fff;background:#0b0e13}
.photo{position:absolute;left:0;top:0;width:100%;height:1180px;object-fit:cover;object-position:${item.focus}}
.shade{position:absolute;left:0;top:0;width:100%;height:1180px;background:linear-gradient(180deg,rgba(11,14,19,.25) 0%,rgba(11,14,19,0) 22%,rgba(11,14,19,0) 42%,rgba(11,14,19,.55) 72%,#0b0e13 98%)}
.logo{position:absolute;left:80px;top:275px;width:276px;filter:drop-shadow(0 2px 12px rgba(0,0,0,.35))}
.label{position:absolute;left:72px;top:932px;display:flex;align-items:center;gap:22px;font-size:25px;font-weight:600;letter-spacing:.24em;text-transform:uppercase;color:#dfe3e7}
.label::before{content:"";width:64px;height:2px;background:#aab2b9}
h1{position:absolute;left:72px;top:992px;font-family:S,serif;font-weight:400;font-size:112px;line-height:1.02;letter-spacing:-.012em}
h1 i{color:#d5dade}
ul{position:absolute;left:72px;right:40px;top:1280px;list-style:none;display:grid;gap:38px}
li{display:flex;align-items:center;gap:22px;font-size:37px;font-weight:400;line-height:1.15;color:#eef1f3}
li::before{content:"";flex:0 0 28px;height:2px;background:#aab2b9}
.web{position:absolute;left:72px;top:1532px;font-size:33px;font-weight:700;letter-spacing:-.005em}
</style></head><body>
<img class="photo" src="${site(item.image)}"><div class="shade"></div><img class="logo" src="${site("logo-ust.webp")}">
<div class="label">${esc(item.label)}</div>
<h1>${item.title.map((s) => esc(s).replace(/\*\*(.+?)\*\*/g, "<i>$1</i>")).join("<br>")}</h1>
<ul>${points}</ul>${item.web === false ? "" : `<div class="web">${web}</div>`}
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
