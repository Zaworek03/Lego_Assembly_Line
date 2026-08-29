// ============================================================
//  Plynne przewijanie z bezwladnoscia (styl Apple)
//  ------------------------------------------------------------
//  Natywne kolko myszy na Windowsie przeskakuje skokowo o ~100 px.
//  Tutaj kazdy tik kolka dokladamy do POZYCJI DOCELOWEJ, a osobna
//  petla rAF dociaga biezaca pozycje do celu wykladniczo - stad
//  wrazenie wytracania predkosci zamiast szarpania.
//
//  Obsluguje oba przypadki naraz:
//    .subtelny-scroll      -> przewijanie w pionie
//    .przewijanie-poziome  -> kolko myszy przesuwa w lewo/prawo
//
//  Delegacja na dokumencie, bo bloki Blazora znikaja i pojawiaja sie
//  przy kazdym renderze - nasluch na konkretnym elemencie przestalby
//  dzialac po przeladowaniu komponentu.
// ============================================================
(function () {
    'use strict';

    // Stala czasowa wygaszania [ms]. Wieksza = dluzszy, bardziej "maslany"
    // poslizg. 78 ms daje ok. 420 ms na jeden zab kolka - plynnie, bez ociezalosci.
    const TAU = 78;
    // Wariant dla wlaczonego "ogranicz animacje" (w Windowsie: Ustawienia ->
    // Ulatwienia dostepu -> Efekty wizualne -> Efekty animacji). Nie wylaczamy
    // poslizgu calkiem - plynne przewijanie bylo wprost zamowione - tylko
    // skracamy go tak, by ruch trwal ~150 ms zamiast ~420 ms.
    const TAU_KROTKIE = 28;
    // Ponizej tej odleglosci [px] dosuwamy na styk i konczymy animacje.
    const PROG_KONCA = 0.5;
    // Ile pikseli na jeden "zab" kolka. Natywne 100 px jest szarpiace,
    // troche wiecej + wygladzanie czyta sie lepiej.
    const MNOZNIK = 1.1;

    // Stan animacji per element (WeakMap - nie trzyma referencji do
    // usunietych juz wezlow DOM).
    const stan = new WeakMap();

    const redukcjaRuchu = window.matchMedia
        ? window.matchMedia('(prefers-reduced-motion: reduce)')
        : { matches: false };

    /** Normalizuje delte kolka do pikseli - przegladarki raportuja linie albo strony. */
    function deltaWPikselach(e, el, poziomo) {
        let d = Math.abs(e.deltaY) >= Math.abs(e.deltaX) ? e.deltaY : e.deltaX;
        if (e.deltaMode === 1) d *= 16;                                   // linie
        else if (e.deltaMode === 2) d *= poziomo ? el.clientWidth : el.clientHeight;  // strony
        return d * MNOZNIK;
    }

    /** Czy element faktycznie ma sie gdzie przewijac w danej osi. */
    function przewijalny(el, poziomo) {
        return poziomo
            ? el.scrollWidth  > el.clientWidth  + 1
            : el.scrollHeight > el.clientHeight + 1;
    }

    function animuj(el, poziomo) {
        const s = stan.get(el);
        if (!s) return;

        const teraz = performance.now();
        const dt = Math.min(teraz - s.czas, 50);   // po przelaczeniu karty dt bywa ogromne
        s.czas = teraz;

        const biezaca = poziomo ? el.scrollLeft : el.scrollTop;
        const roznica = s.cel - biezaca;

        if (Math.abs(roznica) < PROG_KONCA) {
            if (poziomo) el.scrollLeft = s.cel; else el.scrollTop = s.cel;
            stan.delete(el);
            return;
        }

        // Wygaszanie niezalezne od liczby klatek na sekunde: na 144 Hz
        // i na 60 Hz poslizg trwa tyle samo.
        const k = 1 - Math.exp(-dt / (redukcjaRuchu.matches ? TAU_KROTKIE : TAU));
        const nowa = biezaca + roznica * k;
        if (poziomo) el.scrollLeft = nowa; else el.scrollTop = nowa;

        // Element mogl sie skurczyc/zniknac miedzy klatkami.
        if (!el.isConnected) { stan.delete(el); return; }

        s.raf = requestAnimationFrame(() => animuj(el, poziomo));
    }

    document.addEventListener('wheel', function (e) {
        if (e.ctrlKey) return;                 // ctrl+kolko to zoom przegladarki
        if (!e.target || !e.target.closest) return;

        // Pasek poziomy ma pierwszenstwo: jesli kursor jest nad nim,
        // kolko ma przesuwac karty w bok, a nie liste pod spodem.
        let el = e.target.closest('.przewijanie-poziome');
        let poziomo = true;
        if (!el || !przewijalny(el, true)) {
            el = e.target.closest('.subtelny-scroll');
            poziomo = false;
            if (!el || !przewijalny(el, false)) return;   // nie ma czego przewijac
        }

        const max = poziomo
            ? el.scrollWidth  - el.clientWidth
            : el.scrollHeight - el.clientHeight;
        const biezaca = poziomo ? el.scrollLeft : el.scrollTop;

        // Na krancu puszczamy zdarzenie dalej - dzieki temu kolko nad
        // dojechana do konca lista moze obsluzyc rodzica zamiast blokowac.
        const d = deltaWPikselach(e, el, poziomo);
        if ((biezaca <= 0 && d < 0) || (biezaca >= max - 0.5 && d > 0)) return;

        e.preventDefault();

        let s = stan.get(el);
        if (!s) {
            s = { cel: biezaca, czas: performance.now(), raf: 0 };
            stan.set(el, s);
        }
        // Kolejne tiki kumuluja sie w celu - szybkie krecenie daje dluzszy rzut.
        s.cel = Math.max(0, Math.min(max, s.cel + d));

        if (!s.raf) s.raf = requestAnimationFrame(() => animuj(el, poziomo));
    }, { passive: false });
})();
