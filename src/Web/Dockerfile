# syntax=docker/dockerfile:1.7
FROM node:24-alpine AS build
WORKDIR /source
COPY package.json package-lock.json .npmrc ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.29-alpine AS final
ENV API_HOST=http://genaiops-api \
    NGINX_ENVSUBST_FILTER=^API_HOST$
COPY --from=build /source/dist /usr/share/nginx/html
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
EXPOSE 8080
USER 101
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:8080/health || exit 1
