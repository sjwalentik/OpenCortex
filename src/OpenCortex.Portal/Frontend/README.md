# Portal Frontend

This folder is the React + TypeScript frontend workspace for the customer portal.

## Intent

- keep `OpenCortex.Portal` as the ASP.NET host
- keep the classic portal available at `/legacy` while React remains the primary shell
- build the React app into `../wwwroot/app/`

## Current Status

The React app is now the primary portal shell.

- `/` redirects to the React app at `/app/index.html`
- `/app` redirects to the same React entrypoint
- `/legacy` preserves the classic browser portal as a fallback
- signed-in workspace flows now run through React for Documents, Account, Usage, and Tools
- signed-out auth still falls back to the classic sign-in surface

## Next Frontend Steps

1. replace the current document editor surface with Tiptap
2. add graph-aware navigation with React Flow
3. add frontend test coverage once the richer editor shell stabilizes
