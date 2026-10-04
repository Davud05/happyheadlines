# Happy Headlines

Start everything with:

```powershell
docker compose up -d --build
```

On first start (empty databases) the services insert example articles for every continent, comments and drafts.
To start over from scratch: `docker compose down -v` and then `docker compose up -d --build`.

## Services

- ArticleService (load balancer): http://localhost:8000/api/articles
- ArticleService, one continent: http://localhost:8000/api/articles?continent=Europe
- CommentService: http://localhost:8001/api/comments?articleId=a0000000-0000-0000-0000-000008000001
- ProfanityService: http://localhost:8002/api/profanity/words
- DraftService: http://localhost:8003/api/drafts

## Caching

- ArticleCache (Redis, `article-cache`): offline cache with all articles from the last 14 days, loaded by ArticleService on startup and every 5 minutes.
- CommentCache (Redis, `comment-cache`): filled on cache miss, capped at 64 MB and cleaned with LRU.

Responses from both services carry an `X-Cache: HIT` or `X-Cache: MISS` header. If a cache is down, the service falls back to its database.

## Monitoring

- Seq (logs): http://localhost:5380
- Jaeger (traces): http://localhost:16686
- Grafana (cache hit ratio dashboard): http://localhost:3000
- Prometheus (metrics): http://localhost:9090

## Docs

- [C4 context diagram](docs/c4/context.png)
- [C4 container diagram](docs/c4/container.png)
- [ER diagram](docs/er/er-diagram.png)
