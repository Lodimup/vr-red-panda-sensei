# Web client

Install dependencies with `npm install`, then run `npm run dev` from this directory. The development server proxies `/ws` to the server on port 8787. Run `npm run mock` in another terminal to use the development-only mock. The mock emits a tone and scripted snapshots; it is not included in the production bundle.

`npm test` runs the focused unit tests; `npm run build` type-checks and builds the static client. The production server serves the resulting `dist/` directory and `/ws` on the same origin.

Start the session to unlock browser audio. Hold Space to talk, click an object to select it, and drag it onto a region or container to move it. R repeats, H asks for a hint, M opens the menu, and L changes pace. On supported devices, Enter AR opens an immersive AR session.
