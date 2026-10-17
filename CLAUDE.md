## Design
- Modern, clean, dunkel, hochwertig
- Monochrom: Schwarz, Weiß, Grau. Akzent `--accent` (Weiß), Text darauf `--on-accent`
- `--danger` und `--success` nur für Fehler, Löschen und Erfolg
- Glassmorphism: halbtransparente Panels, `backdrop-filter: blur()`, 1px-Ränder `rgba(255,255,255,0.08)`; Blur nur auf wenigen, großen Flächen, nie auf vielen Listenelementen
- Leicht abgerundete Ecken, weiche Schatten, großzügige Abstände
- Schrift: Inter, lokal eingebunden
- Icons als Inline-SVG
- Alle Farben, Radien, Abstände und Animationszeiten als CSS-Variablen in `src/ui/tokens.css`

## Animationen
- Nichts erscheint oder verschwindet abrupt
- Nur `transform` und `opacity` animieren, keine Layout-Properties
- Standard-Easing: `cubic-bezier(0.22, 1, 0.36, 1)`, Dauer 150–300 ms
- Modals: Fade + Scale 0.95 → 1 + leichter Slide nach oben, schließen umgekehrt
- Listen erscheinen gestaffelt (40–60 ms pro Element, max. ~10 Elemente staffeln)
- Hover: `translateY(-2px)` + dezenter Glow; Klick: `scale(0.97)`
- Toasts sliden seitlich ein, mit ablaufender Fortschrittsleiste
- Zahlen auf der Performance-Seite zählen animiert hoch
- Ladezustände mit Skeleton-Shimmer
- Tab- und Seitenwechsel mit Crossfade
- Nur CSS-Transitions und `@keyframes`, keine Libraries
- `prefers-reduced-motion` und eine eigene Einstellung in Settings schalten Animationen fast komplett ab
- Tab-Leiste und Adressleiste reagieren immer sofort, Animationen dürfen Eingaben nie verzögern