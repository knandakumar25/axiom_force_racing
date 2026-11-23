FROM mcr.microsoft.com/playwright:v1.64.1-noble
WORKDIR /app
ENV PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1
RUN npm install --no-save playwright@1.64.1
COPY smoke.cjs ./
USER pwuser
CMD ["node", "smoke.cjs"]
