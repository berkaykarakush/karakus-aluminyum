// Teklif ve sözleşme editörü. Belge içeriği DocumentJson gizli alanında JSON olarak gönderilir.
(() => {
  const form = document.getElementById("doc-form");
  if (!form) return;

  const mode = form.dataset.mode; // "quote" veya "contract"
  const clauses = JSON.parse(document.getElementById("doc-clauses").textContent);
  const designs = JSON.parse(document.getElementById("doc-designs").textContent);
  const SYSTEM = new Set(["musteri", "tarih", "gecerlilik", "belge_no", "toplam", "toplam_yazi", "alici", "alici_adres", "satici", "satici_adres", "konu", "madde_sayisi", "madde_sayisi_yazi", "sayfa_sayisi", "sayfa_sayisi_yazi"]);
  const content = JSON.parse(form.elements.DocumentJson.value || "{}");
  content.variables ||= {};
  content.blocks ||= [];
  content.payments ||= [];
  content.designIds ||= [];
  content.terms ||= [];
  // Sözleşme şablonunun maddelerindeki değişkenler de doldurulabilsin (ör. {{teslim_suresi}}).
  const templateTexts = JSON.parse(document.getElementById("doc-template-texts")?.textContent || "[]");
  content.blocks.forEach((b) => { b.variables ||= {}; b.clauses ||= []; b.items ||= []; });

  const money = (v) => Number(v || 0).toLocaleString("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + " TL";

  function h(tag, attrs, ...children) {
    const el = document.createElement(tag);
    Object.entries(attrs || {}).forEach(([key, value]) => {
      if (key.startsWith("on")) el.addEventListener(key.slice(2), value);
      else if (key in el && key !== "list") el[key] = value;
      else el.setAttribute(key, value);
    });
    children.flat().filter((c) => c !== null && c !== undefined).forEach((c) => el.append(c));
    return el;
  }

  const placeholders = (text) => [...String(text || "").matchAll(/\{\{\s*([\p{L}\p{N}_]+)\s*\}\}/gu)].map((m) => m[1]).filter((n) => !SYSTEM.has(n));
  const blockPlaceholders = (b) => [...new Set(b.clauses.flatMap((c) => placeholders(c.text)).concat(placeholders(b.footer)))];
  const label = (name) => name.replaceAll("_", " ");

  function allPlaceholders() {
    const names = [content.intro, content.closing, content.note, content.subject, content.priceNote, ...content.terms.map((t) => t.text), ...templateTexts].flatMap(placeholders);
    content.blocks.forEach((b) => names.push(...blockPlaceholders(b)));
    return [...new Set(names)];
  }

  function renderVariables() {
    const box = document.getElementById("doc-variables");
    box.textContent = "";
    const names = allPlaceholders();
    if (!names.length) { box.append(h("p", { className: "admin-muted", textContent: "Metinlerde değişken yok." })); return; }
    names.forEach((name) => box.append(h("div", {},
      h("label", { textContent: label(name) }),
      h("input", { value: content.variables[name] || "", "data-variable": name, oninput: (e) => { content.variables[name] = e.target.value; refreshBlockHints(); } }))));
  }

  // Blok alanında boş bırakılan değişken, belge genelindeki değeri kullanır.
  function refreshBlockHints() {
    document.querySelectorAll("[data-block-variable]").forEach((input) => { input.placeholder = content.variables[input.dataset.blockVariable] || "Belge genelindeki değer"; });
  }

  // Fiyatlandırma (yalnızca teklif): maliyet çizimden gelir, fiyat = maliyet × (1 + marj). Elle yazılan fiyat korunur.
  const glassTypes = JSON.parse(document.getElementById("doc-glass-types")?.textContent || "[]");
  const designCosts = new Map(JSON.parse(document.getElementById("doc-design-costs")?.textContent || "[]").map((c) => [c.id, c]));
  const num = (name) => Number(form.elements[name]?.value) || 0;
  const round2 = (v) => Math.round((v + Number.EPSILON) * 100) / 100;
  const priceFromCost = (cost) => round2((Number(cost) || 0) * (1 + num("MarginPercent") / 100));
  const glassPrice = () => glassTypes.find((g) => String(g.id) === form.elements.GlassTypeId?.value)?.pricePerM2 || 0;
  const linked = (item) => item.designId != null && designCosts.has(item.designId);
  const allItems = () => content.blocks.flatMap((b) => b.items);
  // Pozun maliyeti iki parça: imalat (doğrama/panjur) ve cam. Taşerona verilen parça elle girilir; hesaplanamayan pozda sistem parçası sıfırdır.
  const systemBase = (c) => c.problem ? 0 : c.baseCost;
  const systemGlass = (c) => c.problem ? 0 : round2(c.glassAreaM2 * glassPrice());
  function lineCost(item) {
    const c = designCosts.get(item.designId);
    return round2((item.manualBaseCost ?? systemBase(c)) + (item.manualGlassCost ?? systemGlass(c)));
  }

  function reprice() {
    allItems().forEach((item) => {
      if (linked(item)) item.unitCost = lineCost(item);
      if (!item.manualPrice) item.unitPrice = priceFromCost(item.unitCost);
    });
  }

  // Çizim kaleminin maliyet hücresi: imalat ve cam satırı, her birinde "taşeron" seçeneği.
  function costParts(item) {
    const c = designCosts.get(item.designId);
    const part = (label, key, system, forced) => {
      const manual = item[key] != null;
      const changed = () => { item.unitCost = lineCost(item); if (!item.manualPrice) item.unitPrice = priceFromCost(item.unitCost); };
      return h("div", { className: "doc-cost-part" },
        h("span", { textContent: label }),
        manual ? h("input", { type: "number", min: "0", step: "0.01", value: item[key], "aria-label": `${label} taşeron birim maliyeti`, oninput: (e) => { item[key] = Number(e.target.value) || 0; changed(); refreshLine(); } })
          : h("span", { className: "doc-num", textContent: money(system) }),
        h("label", { className: "doc-check", title: forced ? "Maliyet sistemden hesaplanamıyor, elle girin" : "Bu parçayı taşeron yapıyor, maliyeti elle girin" },
          h("input", { type: "checkbox", checked: manual, onchange: (e) => { item[key] = e.target.checked ? system : null; changed(); renderBlocks(); } }), " Taşeron"));
    };
    const showGlass = c.glassAreaM2 > 0 || c.problem || item.manualGlassCost != null;
    return h("td", { className: "doc-cost-cell", title: c.problem || "" },
      part("İmalat", "manualBaseCost", systemBase(c), !!c.problem),
      showGlass ? part("Cam", "manualGlassCost", systemGlass(c), !!c.problem) : null,
      h("div", { className: "doc-cost-total", "data-line-cost": "" }));
  }

  // Yazarken odağı kaybetmemek için yalnızca hesaplanan hücreler güncellenir.
  function refreshLine() {
    document.querySelectorAll("[data-line]").forEach((row) => {
      const item = content.blocks[Number(row.dataset.block)].items[Number(row.dataset.line)];
      const price = row.querySelector("[data-price]");
      if (price && !item.manualPrice) price.value = item.unitPrice;
    });
    renderTotals();
  }

  // Satır tutarı satır iskontosu düşülmüş tutardır (sunucudaki QuoteRules.LineTotal ile aynı).
  const lineTotal = (i) => round2((Number(i.quantity) || 0) * (Number(i.unitPrice) || 0) * (1 - (Number(i.discountRate) || 0) / 100));
  function blockSubtotal(b) { return b.items.filter((i) => mode === "quote" || i.description?.trim()).reduce((sum, i) => sum + lineTotal(i), 0); }
  // Genel iskonto ve KDV: QuoteRules.Calculate ile aynı sıra.
  function grandTotal(subtotal) {
    const discount = round2(subtotal * num("DiscountRate") / 100), net = subtotal - discount, tax = num("TaxRate") / 100;
    return form.elements.VatMode.value === "Added" ? round2(net + round2(net * tax)) : net;
  }
  const lineMargin = (i) => Number(i.unitCost) > 0 ? `%${((Number(i.unitPrice) * (1 - (Number(i.discountRate) || 0) / 100) / Number(i.unitCost) - 1) * 100).toLocaleString("tr-TR", { maximumFractionDigits: 1 })}` : "—";

  // Sözleşme: kalem varsa bedel kalemlerden hesaplanır ve alan kilitlenir; yoksa bedel elle yazılır.
  function renderContractTotal() {
    const items = allItems().filter((i) => i.description?.trim());
    const field = form.elements.TotalAmount, note = document.getElementById("doc-total-note");
    document.querySelectorAll("[data-line]").forEach((row) => {
      const item = content.blocks[Number(row.dataset.block)].items[Number(row.dataset.line)];
      row.querySelector("[data-line-total]").textContent = money(lineTotal(item));
    });
    document.querySelectorAll("[data-block-total]").forEach((el) => { el.textContent = money(blockSubtotal(content.blocks[Number(el.dataset.blockTotal)])); });
    if (!field) return;
    field.readOnly = items.length > 0;
    if (items.length) {
      const subtotal = content.blocks.reduce((sum, b) => sum + blockSubtotal(b), 0);
      field.value = grandTotal(subtotal).toFixed(2);
      field.dispatchEvent(new Event("input"));
    }
    if (note) note.textContent = items.length ? "Kalemlerden hesaplanır (genel iskonto ve KDV dahil genel toplam)." : "Kalem girilmediği için bedel elle yazılır.";
  }

  function renderTotals() {
    const box = document.getElementById("doc-total");
    if (mode === "contract") { renderContractTotal(); return; }
    if (!box) return;
    const subtotal = content.blocks.reduce((sum, b) => sum + blockSubtotal(b), 0);
    box.textContent = `Bloklar toplamı: ${money(subtotal)}`;
    document.querySelectorAll("[data-block-total]").forEach((el) => { el.textContent = money(blockSubtotal(content.blocks[Number(el.dataset.blockTotal)])); });
    document.querySelectorAll("[data-line]").forEach((row) => {
      const item = content.blocks[Number(row.dataset.block)].items[Number(row.dataset.line)];
      row.querySelector("[data-line-total]").textContent = money(lineTotal(item));
      row.querySelector("[data-line-margin]").textContent = lineMargin(item);
      const cost = row.querySelector("[data-line-cost]");
      if (cost) cost.textContent = "Birim: " + money(item.unitCost);
    });

    // Sunucudaki QuoteRules ile aynı hesap; kâr, iskonto sonrası KDV hariç satıştan maliyet düşülerek bulunur.
    const summary = document.getElementById("doc-pricing-summary");
    if (!summary) return;
    const discount = round2(subtotal * num("DiscountRate") / 100), net = subtotal - discount, tax = num("TaxRate") / 100;
    const vat = form.elements.VatMode.value;
    const netExVat = vat === "Included" ? round2(net / (1 + tax)) : net;
    const cost = round2(allItems().filter((i) => i.description?.trim()).reduce((sum, i) => sum + (Number(i.unitCost) || 0) * (Number(i.quantity) || 0), 0));
    // Satır iskontosu marjdan düşer: marj sütunu iskontolu birim fiyata göre hesaplanır.
    const profit = netExVat - cost;
    const rows = [
      ["Toplam maliyet", money(cost)],
      ["Satış (ara toplam)", money(subtotal)],
      discount > 0 ? [`İskonto (%${num("DiscountRate").toLocaleString("tr-TR")})`, "-" + money(discount)] : null,
      ["Net satış" + (vat === "Included" ? " (KDV hariç)" : ""), money(netExVat)],
      vat === "Added" ? ["Müşteriye genel toplam (KDV dahil)", money(round2(net * (1 + tax)))] : vat === "Included" ? ["Müşteriye genel toplam (KDV dahil)", money(net)] : null,
      ["Kâr", money(profit), profit < 0 ? "doc-warn" : "doc-ok"],
      ["Gerçekleşen marj", cost > 0 ? `%${(profit / cost * 100).toLocaleString("tr-TR", { maximumFractionDigits: 1 })}` : "—"]
    ].filter(Boolean);
    summary.textContent = "";
    rows.forEach(([name, value, cls]) => summary.append(h("tr", { className: cls || "" }, h("td", { textContent: name }), h("td", { textContent: value }))));
    const missing = allItems().filter((i) => i.designId != null && designCosts.get(i.designId)?.problem && i.manualBaseCost == null);
    if (missing.length) summary.append(h("tr", { className: "doc-warn" }, h("td", { colSpan: 2, textContent: `${missing.length} pozun maliyeti sistemden hesaplanamıyor (profil sistemi yok veya çizim hatalı). Taşeron kutusunu işaretleyip maliyeti elle girin.` })));
    const sub = allItems().filter((i) => i.manualBaseCost != null || i.manualGlassCost != null).length;
    if (sub) summary.append(h("tr", {}, h("td", { colSpan: 2, className: "admin-muted", textContent: `${sub} kalemde taşeron maliyeti kullanılıyor.` })));
  }

  // Birim fiyatları toplu yuvarlar. Teklifte yuvarlanan fiyat elle yazılmış sayılır (↺ ile marja döner).
  const roundButton = (text, blocks) => window.PriceRound?.bulkButton(text, (fn) => {
    blocks().forEach((b) => b.items.forEach((item) => {
      const rounded = fn(Number(item.unitPrice) || 0);
      if (rounded === Number(item.unitPrice)) return;
      item.unitPrice = rounded;
      if (mode === "quote") item.manualPrice = true;
    }));
    renderBlocks();
  }) ?? null;

  function renderItems(block, index) {
    const body = h("tbody");
    block.items.forEach((item, i) => {
      const update = (key) => (e) => { item[key] = e.target.type === "number" ? Number(e.target.value) : e.target.value; renderTotals(); };
      const costCell = linked(item)
        ? costParts(item)
        : h("td", {}, h("input", { type: "number", min: "0", step: "0.01", value: item.unitCost ?? 0, "aria-label": "Birim maliyet", title: "Elle girilen maliyet", oninput: (e) => { item.unitCost = Number(e.target.value); if (!item.manualPrice) { item.unitPrice = priceFromCost(item.unitCost); priceInput.value = item.unitPrice; } renderTotals(); } }));
      const priceInput = h("input", { type: "number", min: "0", step: "0.01", value: item.unitPrice ?? 0, "aria-label": "Birim fiyat", className: item.manualPrice ? "doc-manual" : "", "data-price": "",
        oninput: (e) => { item.unitPrice = Number(e.target.value); if (!item.manualPrice) { item.manualPrice = true; renderBlocks(); return; } renderTotals(); } });
      const discount = h("td", {}, h("input", { type: "number", min: "0", max: "100", step: "0.01", value: item.discountRate ?? 0, "aria-label": "Satır iskontosu (%)", oninput: update("discountRate") }));
      const basics = [
        h("td", {}, h("input", { value: item.description || "", placeholder: "Örn. Montaj ve nakliye", "aria-label": "Açıklama", oninput: update("description") })),
        h("td", {}, h("input", { type: "number", min: "0", step: "0.001", value: item.quantity ?? 1, "aria-label": "Miktar", oninput: update("quantity") })),
        h("td", {}, h("input", { value: item.unit || "Adet", "aria-label": "Birim", oninput: update("unit") }))];
      if (mode === "contract") {
        body.append(h("tr", { "data-block": index, "data-line": i }, basics,
          h("td", {}, h("input", { type: "number", min: "0", step: "0.01", value: item.unitPrice ?? 0, "aria-label": "Birim fiyat", oninput: update("unitPrice") })),
          discount,
          h("td", { className: "doc-num", "data-line-total": "" }),
          h("td", {}, h("button", { type: "button", className: "doc-link", textContent: "Sil", onclick: () => { block.items.splice(i, 1); renderBlocks(); } }))));
        return;
      }
      body.append(h("tr", { "data-block": index, "data-line": i },
        basics,
        costCell,
        h("td", { className: "doc-price-cell" }, priceInput,
          item.manualPrice ? h("button", { type: "button", className: "doc-link", textContent: "↺", title: "Fiyatı marja göre hesapla", onclick: () => { item.manualPrice = false; item.unitPrice = priceFromCost(item.unitCost); renderBlocks(); } }) : null),
        discount,
        h("td", { className: "doc-num", "data-line-total": "" }),
        h("td", { className: "doc-num", "data-line-margin": "" }),
        h("td", {}, h("button", { type: "button", className: "doc-link", textContent: "Sil", onclick: () => { block.items.splice(i, 1); renderBlocks(); } }))));
    });
    const headers = mode === "contract" ? ["Açıklama", "Miktar", "Birim", "Birim fiyat", "İsk. %", "Tutar", ""] : ["Açıklama", "Miktar", "Birim", "Birim maliyet", "Birim fiyat", "İsk. %", "Tutar", "Marj", ""];
    return h("div", { className: "doc-subpanel" },
      h("h3", { textContent: mode === "contract" ? "İş kalemleri ve birim fiyatlar (sözleşmede fiyat tablosu olarak basılır)" : "Fiyat kalemleri" }),
      h("table", { className: `doc-items ${mode === "contract" ? "doc-items--contract" : "doc-items--priced"}` }, h("thead", {}, h("tr", {}, headers.map((t) => h("th", { textContent: t })))), body),
      h("div", { className: "doc-row" },
        h("button", { type: "button", className: "doc-btn", textContent: "+ Kalem ekle", onclick: () => { block.items.push({ description: "", quantity: 1, unit: "Adet", unitCost: 0, unitPrice: 0, discountRate: 0 }); renderBlocks(); } }),
        block.items.length && content.blocks.length > 1 ? roundButton("Bu bloğu yuvarla", () => [block]) : null,
        h("strong", { "data-block-total": index })));
  }

  function renderClauses(block) {
    const select = h("select", { "aria-label": "Hazır madde" }, h("option", { value: "", textContent: "Hazır madde seçin" }), clauses.map((c) => h("option", { value: c.id, textContent: c.name })));
    const list = block.clauses.map((clause, i) => h("div", { className: "doc-clause" },
      h("input", { value: clause.title || "", placeholder: "Madde adı", "aria-label": "Madde adı", oninput: (e) => { clause.title = e.target.value; } }),
      h("textarea", { rows: 3, value: clause.text || "", "aria-label": "Madde metni", oninput: (e) => { clause.text = e.target.value; }, onchange: () => { renderVariables(); renderBlocks(); } }),
      h("div", { className: "doc-row" },
        h("button", { type: "button", className: "doc-link", textContent: "Yukarı", disabled: i === 0, onclick: () => { block.clauses.splice(i - 1, 0, block.clauses.splice(i, 1)[0]); renderBlocks(); } }),
        h("button", { type: "button", className: "doc-link", textContent: "Sil", onclick: () => { block.clauses.splice(i, 1); renderBlocks(); renderVariables(); } }))));
    return h("div", { className: "doc-subpanel" },
      h("h3", { textContent: "Maddeler" }), list,
      h("div", { className: "doc-row" }, select,
        h("button", { type: "button", className: "doc-btn", textContent: "Ekle", onclick: () => {
          const clause = clauses.find((c) => String(c.id) === select.value);
          block.clauses.push(clause ? { title: clause.name, text: mode === "quote" ? clause.quoteText : clause.contractText } : { title: "", text: "" });
          renderBlocks(); renderVariables();
        } })));
  }

  function renderBlocks() {
    const box = document.getElementById("doc-blocks");
    box.textContent = "";
    content.blocks.forEach((block, index) => {
      const local = blockPlaceholders(block);
      box.append(h("div", { className: "doc-block-card" },
        h("div", { className: "doc-row" },
          h("input", { value: block.name || "", placeholder: "Blok adı (örn. A Blok; tek işte boş bırakılabilir)", "aria-label": "Blok adı", className: "doc-block-title", oninput: (e) => { block.name = e.target.value; } }),
          h("button", { type: "button", className: "doc-btn", textContent: "Bloğu kopyala", onclick: () => { const copy = JSON.parse(JSON.stringify(block)); copy.name = ""; content.blocks.splice(index + 1, 0, copy); renderBlocks(); } }),
          content.blocks.length > 1 ? h("button", { type: "button", className: "doc-link", textContent: "Bloğu sil", onclick: () => { content.blocks.splice(index, 1); renderBlocks(); renderVariables(); } }) : null),
        local.length ? h("div", { className: "doc-subpanel" },
          h("h3", { textContent: "Bu bloğa özel değerler (ör. adet)" }),
          h("div", { className: "doc-grid" }, local.map((name) => h("div", {},
            h("label", { textContent: label(name) }),
            h("input", { value: block.variables[name] || "", "data-block-variable": name, oninput: (e) => { block.variables[name] = e.target.value; } }))))) : null,
        renderClauses(block),
        mode === "contract" ? h("div", { className: "doc-subpanel" },
          h("h3", { textContent: "Blok kapanış metni (isteğe bağlı)" }),
          h("textarea", { rows: 2, value: block.footer || "", "aria-label": "Blok kapanış metni", oninput: (e) => { block.footer = e.target.value; }, onchange: () => { renderBlocks(); renderVariables(); } })) : null,
        renderItems(block, index)));
    });
    refreshBlockHints();
    renderTotals();
  }

  // Şartlar: ödeme, teslim, montaj, hariç tutulanlar, garanti... Proformada "Teklif şartları", sözleşmede "Özel şartlar".
  function renderTerms() {
    const box = document.getElementById("doc-terms");
    if (!box) return;
    box.textContent = "";
    content.terms.forEach((term, i) => box.append(h("div", { className: "doc-clause" },
      h("input", { value: term.title || "", placeholder: "Başlık (örn. Teslim süresi)", "aria-label": `${i + 1}. şart başlığı`, oninput: (e) => { term.title = e.target.value; } }),
      h("textarea", { rows: 2, value: term.text || "", "aria-label": `${i + 1}. şart metni`, oninput: (e) => { term.text = e.target.value; }, onchange: renderVariables }),
      h("div", { className: "doc-row" },
        h("button", { type: "button", className: "doc-link", textContent: "Yukarı", disabled: i === 0, onclick: () => { content.terms.splice(i - 1, 0, content.terms.splice(i, 1)[0]); renderTerms(); } }),
        h("button", { type: "button", className: "doc-link", textContent: "Sil", onclick: () => { content.terms.splice(i, 1); renderTerms(); renderVariables(); } })))));
    box.append(h("button", { type: "button", className: "doc-btn", textContent: "+ Şart ekle", onclick: () => { content.terms.push({ title: "", text: "" }); renderTerms(); } }));
  }

  function renderPayments() {
    const box = document.getElementById("doc-payments");
    if (!box) return;
    box.textContent = "";
    const total = () => Number(form.elements.TotalAmount.value) || 0;
    const status = h("p", { id: "doc-payment-status" });
    const updateStatus = () => {
      const sum = content.payments.reduce((s, p) => s + (Number(p.amount) || 0), 0);
      const ok = Math.abs(sum - total()) < 0.005;
      status.className = ok ? "doc-ok" : "doc-warn";
      status.textContent = ok ? `Ödeme planı toplamı bedele eşit: ${money(sum)}` : `Ödeme planı ${money(sum)}, bedel ${money(total())}. İmzadan önce eşitlenmelidir.`;
    };
    content.payments.forEach((payment, i) => {
      const needsText = payment.kind === "barter" || payment.kind === "other";
      box.append(h("div", { className: "doc-payment" },
        h("div", {}, h("label", { textContent: "Tip" }), h("select", { onchange: (e) => { payment.kind = e.target.value; renderPayments(); } },
          [["cash", "Peşin"], ["check", "Çek"], ["barter", "Barter"], ["other", "Diğer"]].map(([value, text]) => h("option", { value, textContent: text, selected: payment.kind === value })))),
        h("div", {}, h("label", { textContent: "Tutar (TL)" }), h("input", { type: "number", min: "0", step: "0.01", value: payment.amount ?? 0, oninput: (e) => { payment.amount = Number(e.target.value); updateStatus(); } })),
        payment.kind === "check" ? h("div", {}, h("label", { textContent: "Çek adedi" }), h("input", { type: "number", min: "1", value: payment.count ?? 1, oninput: (e) => { payment.count = Number(e.target.value); } })) : null,
        h("div", { className: "doc-payment-text" }, h("label", { textContent: needsText ? "Açıklama" : payment.kind === "check" ? "Çek ifadesi (boşsa \"N adet çekle\")" : "Açıklama (boşsa \"peşin olarak ödenecektir\")" }),
          h("textarea", { rows: 2, value: payment.text || "", placeholder: payment.kind === "barter" ? "Örn. {{ada}} ada, {{parsel}} parselde kayıtlı inşaatta 4 numaralı daire" : payment.kind === "check" ? "Örn. sıralı 5 ay vadeli çeklerle" : "", oninput: (e) => { payment.text = e.target.value; } })),
        h("button", { type: "button", className: "doc-link", textContent: "Sil", onclick: () => { content.payments.splice(i, 1); renderPayments(); } })));
    });
    box.append(h("button", { type: "button", className: "doc-btn", textContent: "+ Ödeme ekle", onclick: () => { content.payments.push({ kind: "cash", amount: 0, count: 1, text: "" }); renderPayments(); } }), status);
    form.elements.TotalAmount.oninput = updateStatus;
    updateStatus();
  }

  function renderDesigns() {
    const box = document.getElementById("doc-designs-list");
    box.textContent = "";
    if (!designs.length) { box.append(h("p", { className: "admin-muted", textContent: "Henüz çizim yok." })); return; }
    // Çizimler listeleriyle gruplanır: liste başlığı içindeki tüm çizimleri seçer.
    const lists = new Map();
    designs.forEach((design) => { if (!lists.has(design.listId)) lists.set(design.listId, []); lists.get(design.listId).push(design); });
    lists.forEach((items) => {
      const allChecked = items.every((design) => content.designIds.includes(design.id));
      box.append(h("label", { className: "doc-check" },
        h("input", { type: "checkbox", checked: allChecked, onchange: (e) => {
          const ids = items.map((design) => design.id);
          content.designIds = content.designIds.filter((id) => !ids.includes(id)).concat(e.target.checked ? ids : []);
          renderDesigns();
        } }),
        h("strong", { textContent: ` ${items[0].listName}` })));
      items.forEach((design) => box.append(h("label", { className: "doc-check", style: "margin-left:18px" },
        h("input", { type: "checkbox", checked: content.designIds.includes(design.id), onchange: (e) => {
          content.designIds = e.target.checked ? content.designIds.concat(design.id) : content.designIds.filter((id) => id !== design.id);
          renderDesigns();
        } }),
        ` ${design.name} (${Units.format(design.widthMm)} x ${Units.format(design.heightMm)} ${Units.code}, ${design.quantity} adet)`)));
    });
  }

  // Sözleşme alanları, müşteri seçilince adres ve vergi bilgisiyle dolar; yazılanın üstüne yazmaz.
  const customers = JSON.parse(document.getElementById("doc-customers")?.textContent || "[]");
  if (mode === "contract") form.elements.CustomerId.addEventListener("change", (e) => {
    const customer = customers.find((c) => String(c.id) === e.target.value);
    if (!customer) return;
    if (!form.elements.BuyerName.value) form.elements.BuyerName.value = customer.name || "";
    if (!form.elements.BuyerAddress.value) form.elements.BuyerAddress.value = customer.address || "";
    if (!form.elements.BuyerTaxInfo.value) form.elements.BuyerTaxInfo.value = customer.taxInfo || "";
  });

  document.querySelectorAll("[data-content]").forEach((input) => {
    const key = input.dataset.content;
    input.value = content[key] || "";
    input.addEventListener("input", () => { content[key] = input.value; });
    input.addEventListener("change", renderVariables);
  });
  document.getElementById("doc-add-block").addEventListener("click", () => {
    content.blocks.push({ name: "", variables: {}, clauses: [], items: mode === "quote" ? [{ description: "", quantity: 1, unit: "Adet", unitCost: 0, unitPrice: 0, discountRate: 0 }] : [] });
    renderBlocks();
  });
  const addBlock = document.getElementById("doc-add-block");
  const roundAll = roundButton("Tüm fiyatları yuvarla", () => content.blocks);
  if (roundAll) addBlock.after(roundAll);
  form.addEventListener("submit", () => { form.elements.DocumentJson.value = JSON.stringify(content); });
  if (mode === "contract") ["DiscountRate", "TaxRate"].forEach((name) => form.elements[name]?.addEventListener("input", renderTotals));
  if (mode === "contract") form.elements.VatMode?.addEventListener("change", renderTotals);
  if (mode === "quote") {
    ["MarginPercent", "GlassTypeId"].forEach((name) => form.elements[name]?.addEventListener(name === "GlassTypeId" ? "change" : "input", () => { reprice(); renderBlocks(); }));
    ["DiscountRate", "TaxRate", "VatMode"].forEach((name) => form.elements[name]?.addEventListener("input", renderTotals));
    form.elements.VatMode?.addEventListener("change", renderTotals);
  }

  renderVariables();
  renderBlocks();
  renderTerms();
  renderPayments();
  renderDesigns();
})();
