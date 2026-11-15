# syntax=docker/dockerfile:1
FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts && npm audit --audit-level=high
COPY . .
RUN npm run type-check && npm run test && npm run build

FROM node:24-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production
ENV HOST=0.0.0.0
ENV PORT=3000
# Pull Alpine security fixes that are not yet in the base image (e.g. zlib CVE-2026-4738).
# The runtime only needs node: drop the bundled package managers (smaller image, fewer CVEs)
RUN apk upgrade --no-cache \
    && rm -rf /usr/local/lib/node_modules/npm /usr/local/lib/node_modules/corepack /opt/yarn-* \
      /usr/local/bin/npm /usr/local/bin/npx /usr/local/bin/corepack /usr/local/bin/yarn /usr/local/bin/yarnpkg \
    && addgroup -S vernetzt && adduser -S vernetzt -G vernetzt
COPY --from=build --chown=vernetzt:vernetzt /app/.output ./.output
USER vernetzt
EXPOSE 3000
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://127.0.0.1:3000/health >/dev/null || exit 1
CMD ["node", ".output/server/index.mjs"]
