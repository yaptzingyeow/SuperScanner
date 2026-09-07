# syntax=docker/dockerfile:1.7
FROM node:22.22.3-alpine AS build
WORKDIR /source
COPY apps/web/package.json apps/web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY apps/web/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.28-alpine AS final
ENV API_UPSTREAM=http://api:8080 \
    R2_CONNECT_SOURCE=https://*.r2.cloudflarestorage.com
COPY apps/web/nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build --chown=101:101 /source/dist/web/browser /usr/share/nginx/html
EXPOSE 8080
