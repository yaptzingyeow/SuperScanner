# syntax=docker/dockerfile:1.7
FROM node:22.22.3-alpine AS build
WORKDIR /source
COPY apps/web/package.json apps/web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY apps/web/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.28-alpine AS final
# ADS_*: extra Content Security Policy sources for Google AdSense, empty (no ads allowed) unless set on the
# web service; see docs/operations/ads.md for the values. They must be defined so the template substitutes them.
ENV API_UPSTREAM=http://api:8080 \
    DNS_RESOLVER=127.0.0.11 \
    R2_CONNECT_SOURCE=https://*.r2.cloudflarestorage.com \
    ADS_SCRIPT_SRC= \
    ADS_FRAME_SRC= \
    ADS_CONNECT_SRC= \
    ADS_IMG_SRC=
COPY apps/web/nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build --chown=101:101 /source/dist/web/browser /usr/share/nginx/html
EXPOSE 8080
