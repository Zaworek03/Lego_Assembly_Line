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
    // poslizg. 312 ms daje ok. 1,7 s na jeden zab kolka - mocna, dluga bezwladnosc.
    const TAU = 312;
    // Wariant dla wlaczonego "ogranicz animacje" (w Windowsie: Ustawienia ->
    // Ulatwienia dostepu -> Efekty wizualne -> Efekty animacji). Nie wylaczamy
    // poslizgu calkiem - plynne przewijanie bylo wprost zamowione - tylko
    // skracamy go tak, by ruch trwal ~150 ms zamiast ~420 ms.
    const TAU_KROTKIE = 112;
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


// ============================================================
//  Animowane liczniki
//  ------------------------------------------------------------
//  Blazor podmienia liczbe skokowo; tutaj dojezdzamy do niej
//  plynnie - najpierw przyspieszenie, potem wyhamowanie na celu.
//
//  UWAGA - tu byl blad, ktory zawiesil przegladarke i maszyne.
//  Poprzednia wersja pilnowala sie flaga ustawiana synchronicznie
//  wokol wlasnego zapisu. To NIE dziala: callback MutationObserver
//  leci w mikrozadaniu, wiec flaga byla juz skasowana, obserwator
//  widzial wlasny zapis jako zmiane "z zewnatrz" i startowal kolejna
//  animacje. Liczba petli rAF rosla wykladniczo.
//
//  Teraz sa trzy niezalezne bariery:
//   1) Porownanie tresci z ostatnim tekstem, ktory sami wpisalismy
//      (WeakMap, nie zalezy od momentu wywolania callbacku).
//   2) JEDNA petla rAF na cala strone - nie da sie ich rozmnozyc,
//      bo `raf` jest pojedyncza zmienna modulu.
//   3) Staly czas trwania animacji + limit liczby elementow.
// ============================================================
(function () {
    'use strict';

    const CZAS       = 600;   // [ms] pelny przejazd do wartosci docelowej
    const CZAS_KROTKI = 180;  // przy wlaczonym "ogranicz animacje"
    const MAX_ELEMENTOW = 32; // ponad to wpisujemy od razu, bez animacji
    const OKNO_STARTU = 1200; // [ms] cisza po pojawieniu sie elementu na stronie

    const aktywne   = new Map();      // el -> opis biezacej animacji
    const naszZapis = new WeakMap();  // el -> ostatni tekst wpisany PRZEZ NAS
    const poprzednia = new WeakMap(); // el -> ostatnia znana wartosc liczbowa
    const pierwszeWidzenie = new WeakMap(); // el -> kiedy element pojawil sie na stronie
    let raf = 0;                      // bariera 2): jedyny uchwyt rAF

    const redukcjaRuchu = window.matchMedia
        ? window.matchMedia('(prefers-reduced-motion: reduce)')
        : { matches: false };

    // Liczba moze byc otoczona tekstem ("85%", "12 szt").
    const RE = /^(\D*?)(-?\d+(?:[.,]\d+)?)(.*)$/s;

    /**
     * Zapis przez nodeValue, a nie textContent: textContent niszczy wezel
     * tekstowy, a Blazor trzyma do niego referencje i przy nastepnym
     * renderze nie znalazlby czego podmienic.
     */
    function pisz(el, tekst) {
        const w = el.firstChild;
        if (w && w.nodeType === 3 && el.childNodes.length === 1) w.nodeValue = tekst;
        else el.textContent = tekst;
        naszZapis.set(el, tekst);
    }

    /** Przyspiesza, potem hamuje - dokladnie o to chodzilo. */
    function wygladz(p) {
        return p < 0.5 ? 4 * p * p * p : 1 - Math.pow(-2 * p + 2, 3) / 2;
    }

    function tik(teraz) {
        raf = 0;
        for (const [el, a] of aktywne) {
            if (!el.isConnected) { aktywne.delete(el); continue; }

            const p = (teraz - a.start) / a.czas;
            if (p >= 1) { pisz(el, a.docelowyTekst); aktywne.delete(el); continue; }

            const v = a.od + (a.doo - a.od) * wygladz(p);
            pisz(el, a.prefiks + v.toFixed(a.miejsca).replace('.', a.separator) + a.sufiks);
        }
        if (aktywne.size) raf = requestAnimationFrame(tik);
    }

    function obsluz(el) {
        const tekst = el.textContent;

        // Bariera 1): to nasz wlasny zapis z poprzedniej klatki - ignorujemy.
        if (naszZapis.get(el) === tekst) return;

        const m = RE.exec(tekst);
        if (!m) { poprzednia.delete(el); return; }

        const [, prefiks, liczba, sufiks] = m;
        const separator = liczba.includes(',') ? ',' : '.';
        const doo = parseFloat(liczba.replace(',', '.'));
        if (!isFinite(doo)) return;

        const od = poprzednia.get(el);
        poprzednia.set(el, doo);

        const teraz = performance.now();
        let odKiedy = pierwszeWidzenie.get(el);
        if (odKiedy === undefined) { pierwszeWidzenie.set(el, teraz); odKiedy = teraz; }

        // Pierwsze pojawienie sie elementu albo brak zmiany - nie animujemy.
        if (od === undefined || od === doo) return;

        // Okno wyciszenia po wejsciu na strone. Blazor renderuje pulpit
        // najpierw z zerami i dopiero potem wstawia dane z bazy, w dodatku
        // partiami z kilku zapytan. Bez tego kazdy powrot na strone glowna
        // rozpedzal wszystkie liczniki od zera, i to po kilka razy pod rzad.
        if (teraz - odKiedy < OKNO_STARTU) return;

        if (aktywne.size >= MAX_ELEMENTOW) return;

        const kropka = liczba.indexOf('.') >= 0 ? liczba.indexOf('.') : liczba.indexOf(',');
        aktywne.set(el, {
            od, doo, prefiks, sufiks, separator,
            miejsca: kropka < 0 ? 0 : liczba.length - kropka - 1,
            docelowyTekst: tekst,
            start: performance.now(),
            czas: redukcjaRuchu.matches ? CZAS_KROTKI : CZAS
        });
        if (!raf) raf = requestAnimationFrame(tik);
    }

    new MutationObserver(zmiany => {
        const dotkniete = new Set();
        for (const z of zmiany) {
            const w = z.type === 'characterData' ? z.target.parentElement : z.target;
            const el = w && w.closest ? w.closest('.licznik') : null;
            if (el) dotkniete.add(el);
        }
        dotkniete.forEach(obsluz);
    }).observe(document.body, { subtree: true, childList: true, characterData: true });
})();

// ============================================================
//  Przewijanie do wskazanego elementu
//  ------------------------------------------------------------
//  Wejscie z kafla "Przegladu raportow" ma nie tylko otworzyc
//  zakladke, ale i dojechac do konkretnego raportu.
//  Male opoznienie zamiast requestAnimationFrame: Blazor dopisuje
//  rozwiniete sekcje dopiero po renderze, a rAF w karcie w tle bywa
//  zamrozony i przewijanie w ogole by nie ruszylo.
// ============================================================
window.przewinDoElementu = function (id) {
    setTimeout(() => {
        const el = document.getElementById(id);
        if (!el) return;

        // block:center, a nie start - raport ma wyladowac na srodku ekranu,
        // a nie przykleic sie gorna krawedzia pod naglowkiem.
        el.scrollIntoView({ behavior: "smooth", block: "center" });

        // Dwa subtelne mrugniecia, zeby oko zlapalo, ktora karta to ta wybrana.
        // Klase zdejmujemy po animacji, inaczej ponowne wejscie w ten sam
        // raport juz by nie mrugnelo (animacja nie startuje drugi raz).
        el.classList.remove("raport-wskazany");
        void el.offsetWidth;                      // wymuszenie przeliczenia stylu
        el.classList.add("raport-wskazany");
        setTimeout(() => el.classList.remove("raport-wskazany"), 1600);
    }, 80);
};
