# The counter screen

The interface a branch representative uses while a customer is on the telephone
asking whether a part is in stock and what it costs them.

Everything here follows from that sentence. The person using it has a telephone
against one shoulder, so every journey is completable from the keyboard and the
answer fits on one screen without scrolling. They are going to repeat what they
read to somebody, so the interface never shows a figure without saying where it
came from — and never lets two different answers look the same.

## The distinction the whole thing is built around

"Nothing matched", "the system could not be reached" and "stock is not recorded
for this part" are three different answers. Collapsed into an empty result they
look identical, and a representative reading that would tell a customer there is
no stock — which is a statement someone will act on.

So each has its own state, its own wording and its own colour, and the API
carries a typed failure the screen switches on rather than a shape the screen has
to guess from.

## Running it

Started with everything else, never on its own:

```powershell
../scripts/run-dev-clean.ps1
```

The dev server proxies `/api` to the API, so the browser only ever sees one
origin.

## The suites

```powershell
npm test        # Vitest: the copied summary, the record rendering
npm run cypress # a real browser, real events, axe-core on every screen
```

The Cypress suite uses `cypress-real-events` throughout, and that is not a
stylistic preference. A synthetic keypress does not move focus, so a suite built
on `cy.type()` would report that keyboard navigation works without ever having
exercised it — and keyboard operation is the requirement, not a courtesy.

Both themes are defined at token level, and both meet AA contrast. A colour whose
only definition sits inside a media query never applies in the other theme, which
is how an interface ends up showing one theme's text on the other theme's
background.
