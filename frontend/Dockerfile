FROM node:20-alpine AS build

WORKDIR /app

COPY package*.json ./

RUN npm ci

COPY . .

ARG VITE_API_URL=/api/v1
ARG VITE_MAPTILER_API_KEY
ENV VITE_API_URL=$VITE_API_URL
ENV VITE_MAPTILER_API_KEY=$VITE_MAPTILER_API_KEY

RUN npm run build

FROM nginx:stable-alpine AS production

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
