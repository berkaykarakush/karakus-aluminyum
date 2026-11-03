// "social media/bilgi-icerikleri" gönderi görsellerini (1080×1350) üretir. Öne çıkanlara ve referans projelere dokunmaz;
// referans projeler panelden yüklenir ve tarayıcıda üretilir.
// Düzen: tam ekran fotoğraf, sol üstte kısa başlık, altta ortalanmış logo; telefon ve uzun metin yok.
// Açıklamalar "social media/bilgi-icerikleri/icerik.json" içindedir; bu betik yalnızca görselleri yazar.
//
// Çalıştırma (repo kökünden):
//   docker run --rm --user "$(id -u):$(id -g)" -e HOME=/tmp -e REPO=/repo -v "$PWD":/repo -w /tmp mcr.microsoft.com/playwright:v1.56.1-noble \
//     bash -c 'npm i -s playwright@1.56.1 >/dev/null 2>&1 && cp /repo/scripts/sosyal-medya/render.mjs . && node render.mjs'
// Yalnızca bazı dosyalar için sona dosya adlarını ekleyin: ... node render.mjs 03-cam-balkon-katlanir-surme.jpg

import { chromium } from "playwright";
import { existsSync, writeFileSync } from "node:fs";
import os from "node:os";
import { pathToFileURL } from "node:url";
import path from "node:path";

const root = process.env.REPO ?? path.resolve(import.meta.dirname, "../.."); // betik kopyalanarak çalıştırılır
const out = path.join(root, "social media", "bilgi-icerikleri");
const site = (name) => path.join(root, "wwwroot/images", name);
const font = (name) => pathToFileURL(path.join(root, "wwwroot/fonts", name)).href;

// title: satırlar; **kalın** vurgu. icons: en fazla üç kısa özellik. focus: fotoğrafın yatay odağı (object-position).
const posts = [
  { file: "01-villa-otomatik-giyotin.jpg", image: site("otomatik-giyotin-camlama-sistemleri.webp"), focus: "60% 50%",
    title: ["Terasınız", "**dört mevsim**", "sizin."], text: "Otomatik giyotin cam, tek dokunuşla açılır ve kapanır.",
    icons: [["motor", "Motorlu kullanım"], ["wind", "Rüzgâr ve yağmura karşı"], ["view", "Kesintisiz manzara"]] },
  { file: "02-kafe-restoran-teras.jpg", image: site("manuel-giyotin-ruzgar-kirici-sistemleri.webp"), focus: "45% 50%",
    title: ["Rüzgârlı akşamlar", "**masaları**", "**boşaltmasın.**"], text: "Giyotin cam ve rüzgâr kırıcılarla teras alanınız daha uzun süre kullanımda kalır." },
  { file: "03-cam-balkon-katlanir-surme.jpg", image: site("cam-balkon.webp"), focus: "55% 50%",
    title: ["**Katlanır** mı,", "**sürme** mi?"], text: "Balkonunuzun ölçüsüne ve kullanımına uygun sistemi birlikte seçelim." },
  { file: "04-aluminyum-mu-pvc-mi.jpg", image: site("pvc-dograma.webp"), focus: "96% 50%",
    title: ["Alüminyum mu,", "**PVC mi?**"], text: "Doğru seçim ölçüye, mimariye ve beklentinize göre netleşir.",
    icons: [["ruler", "Açıklık ölçüsü"], ["thermo", "Yalıtım beklentisi"], ["coin", "Bütçe"]] },
  { file: "05-mimar-muteahhit.jpg", image: site("giydirme-cephe-sistemleri.webp"), focus: "55% 50%",
    title: ["Doğrama kararı", "**projenin başında**", "verilir."], text: "Açıklık, lento ve panjur kutusu detaylarını çizim aşamasında birlikte planlayalım." },
  { file: "06-ofis-bolme.jpg", image: site("ofis-bolme-sistemleri.webp"), focus: "50% 50%",
    title: ["Açık ofise", "**kapalı toplantı**", "**odası.**"], text: "Cam bölme sistemleri gün ışığını kesmeden alanı ayırır." },
  { file: "07-fabrika-depo.jpg", image: site("endustriyel-ve-garaj-kapi-sistemleri.webp"), focus: "50% 50%",
    title: ["Yükleme alanı", "**hız kesmesin.**"], text: "Kapı, rampa ve körük sistemlerini kullanım yoğunluğunuza göre seçiyoruz.",
    icons: [["door", "Endüstriyel kapı"], ["ramp", "Rampa"], ["fold", "Körük"]] },
  { file: "08-fotoselli-kapi.jpg", image: site("otomatik-kapi-ve-diger-sistemler.webp"), focus: "62% 50%",
    title: ["Her gün", "**yüzlerce geçiş,**", "tek giriş."], text: "Fotoselli ve döner kapılar yoğun girişlerde konforlu kullanım sağlar." },
  { file: "09-site-yonetimi-giris.jpg", image: site("bahce-kapisi-ve-bariyer-sistemleri.webp"), focus: "50% 50%",
    title: ["Site girişinde", "**kontrol sizde.**"], text: "Bahçe kapısı ve bariyerleri giriş genişliğine ve geçiş yoğunluğuna göre planlıyoruz." },
  { file: "10-olcuden-uygulamaya.jpg", image: site("kapi-ve-pencere-sistemleri.webp"), focus: "50% 50%", label: "2001'den beri İzmir'de",
    title: ["Ölçüden", "**uygulamaya.**"], steps: ["Dinliyoruz", "Ölçüyoruz", "Teklif veriyoruz", "Uyguluyoruz"] },
  { file: "11-yagmura-hazir-mi.jpg", image: site("yana-kayar-surme-cam-sistemleri.webp"), focus: "70% 50%",
    title: ["Projeniz", "**yağmura**", "gerçekten", "hazır mı?"], text: "Yoğun yağışta performans; doğru sistem seçimi ve özenli montajla başlar.",
    icons: [["drop", "Su sızdırmazlık"], ["wind", "Rüzgâr dayanımı"], ["shield", "Uzun ömürlü kullanım"]] },
  { file: "12-soguklari-disarida-birakin.jpg", image: site("katlanir-cam-sistemleri.webp"), focus: "50% 50%",
    title: ["Soğukları", "**dışarıda**", "bırakın."], text: "Doğru cam ve doğrama seçimiyle kış aylarında da daha fazla konfor." },
  { file: "13-projeniz-ne-istiyor.jpg", image: site("sürme-sistemleri.webp"), focus: "75% 50%",
    title: ["Projeniz", "**ne istiyor?**"], text: "Konut, ticari yapı ya da sanayi tesisi; sistemi projenin ihtiyacına göre belirliyoruz.",
    icons: [["home", "Konut"], ["office", "Ticari yapı"], ["factory", "Sanayi"]] },
];

const icon = {
  motor: '<circle cx="12" cy="12" r="8"/><path d="M12 7v5l3 2"/>',
  wind: '<path d="M3 9h11a3 3 0 1 0-3-3M3 15h15a3 3 0 1 1-3 3"/>',
  view: '<path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
  ruler: '<path d="M3 17 17 3l4 4L7 21z"/><path d="m7 13 2 2m1-5 2 2m1-5 2 2"/>',
  thermo: '<path d="M10 14V5a2 2 0 1 1 4 0v9a4 4 0 1 1-4 0z"/>',
  coin: '<circle cx="12" cy="12" r="9"/><path d="M9 9h5a2 2 0 0 1 0 4h-4a2 2 0 0 0 0 4h5M12 6v12"/>',
  door: '<path d="M4 21V4h16v17M4 9h16M4 14h16"/>',
  ramp: '<path d="M2 19h20M4 19l10-7h8"/>',
  fold: '<path d="M4 4v16l5-3V7zM9 7l6-3v16l-6-3M15 4l5 3v10l-5 3"/>',
  drop: '<path d="M12 3s6 7 6 11a6 6 0 0 1-12 0c0-4 6-11 6-11z"/>',
  shield: '<path d="M12 3 4 6v6c0 5 3.5 8 8 9 4.5-1 8-4 8-9V6z"/><path d="m9 12 2 2 4-4"/>',
  home: '<path d="M3 11 12 4l9 7M5 10v10h14V10"/>',
  office: '<path d="M5 21V3h14v18M9 7h2m2 0h2m-6 4h2m2 0h2m-6 4h2m2 0h2"/>',
  factory: '<path d="M3 21V10l6 4v-4l6 4V5h4v16z"/>',
};

const esc = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;");
const line = (s) => esc(s).replace(/\*\*(.+?)\*\*/g, "<b>$1</b>");
const src = (p) => pathToFileURL(path.isAbsolute(p) ? p : path.join(out, p)).href;

function html(item, kind) {
  const title = item.title ? `<h1>${item.title.map(line).join("<br>")}</h1>` : "";
  const icons = item.icons ? `<ul class="icons">${item.icons.map(([k, t]) => `<li><svg viewBox="0 0 24 24">${icon[k]}</svg><span>${esc(t)}</span></li>`).join("")}</ul>` : "";
  const steps = item.steps ? `<ol class="steps">${item.steps.map((t, i) => `<li><em>0${i + 1}</em>${esc(t)}</li>`).join("")}</ol>` : "";
  const copy = `<div class="copy">${item.label ? `<p class="label">${esc(item.label)}</p>` : ""}${title}${item.text ? `<p class="text">${esc(item.text)}</p>` : ""}${icons}${steps}</div>`;
  return `<!doctype html><html lang="tr"><head><meta charset="utf-8"><style>
@font-face{font-family:M;src:url("${font("manrope-latin.woff2")}") format("woff2");font-weight:200 800;unicode-range:U+0000-00FF,U+0131,U+2000-206F}
@font-face{font-family:M;src:url("${font("manrope-latin-ext.woff2")}") format("woff2");font-weight:200 800;unicode-range:U+0100-02BA,U+1E00-1EFF}
*{margin:0;padding:0;box-sizing:border-box}
body{width:1080px;height:1350px;overflow:hidden;position:relative;font-family:M,sans-serif;color:#fff;background:#0d1115}
.photo{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;object-position:${item.focus ?? "50% 50%"}}
.shade{position:absolute;inset:0;background:linear-gradient(180deg,rgba(8,11,14,.8) 0%,rgba(8,11,14,.5) 36%,rgba(8,11,14,0) 56%,rgba(8,11,14,0) 76%,rgba(8,11,14,.55) 100%)}
.copy{position:absolute;left:84px;right:84px;top:96px}
.label{font-size:30px;font-weight:600;color:#9cc8e2;letter-spacing:.01em;margin-bottom:14px}
h1{font-size:78px;line-height:1.08;font-weight:300;letter-spacing:-.02em;text-shadow:0 2px 24px rgba(0,0,0,.25)}
h1 b{font-weight:800}
.text{margin-top:28px;max-width:640px;font-size:30px;line-height:1.4;font-weight:500;color:rgba(255,255,255,.95);text-shadow:0 1px 14px rgba(0,0,0,.45)}
.icons{display:flex;margin-top:40px;list-style:none}
.icons li{display:flex;flex-direction:column;gap:12px;width:180px;padding:0 22px;border-left:1.5px solid rgba(255,255,255,.55);font-size:19px;font-weight:700;line-height:1.25;text-transform:uppercase;letter-spacing:.04em}
.icons li:first-child{padding-left:0;border-left:0}
.icons svg{width:44px;height:44px;fill:none;stroke:#fff;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round}
.steps{display:grid;grid-template-columns:repeat(4,1fr);gap:18px;margin-top:44px;list-style:none;max-width:900px}
.steps li{padding-top:16px;border-top:2px solid rgba(255,255,255,.7);font-size:24px;font-weight:700;line-height:1.25}
.steps em{display:block;font-style:normal;font-size:20px;font-weight:600;color:#9cc8e2;margin-bottom:6px}
.logo{position:absolute;left:50%;bottom:64px;width:270px;transform:translateX(-50%);filter:drop-shadow(0 2px 14px rgba(0,0,0,.45))}
</style></head><body><img class="photo" src="${src(item.image)}"><div class="shade"></div>${copy}<img class="logo" src="${src(site("logo-ust.webp"))}"></body></html>`;
}

const only = process.argv.slice(2);
const jobs = posts.map((p) => [p, "post"])
  .filter(([p]) => only.length === 0 || only.includes(p.file));
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1080, height: 1350 } });
for (const [item, kind] of jobs) {
  if (!existsSync(path.isAbsolute(item.image) ? item.image : path.join(out, item.image))) { console.warn("fotoğraf yok:", item.image); continue; }
  // Yerel fotoğraf ve yazı tipleri yalnızca file:// adresinden açılan sayfaya yüklenir.
  const temp = path.join(os.tmpdir(), "sosyal-medya.html");
  writeFileSync(temp, html(item, kind));
  await page.goto(pathToFileURL(temp).href, { waitUntil: "load" });
  await page.evaluate(() => document.fonts.ready);
  const target = path.join(out, item.file);
  await page.screenshot({ path: target, type: "jpeg", quality: 88 });
  console.log("yazıldı:", path.relative(root, target));
}
await browser.close();
