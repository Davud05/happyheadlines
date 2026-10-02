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

## Monitoring

- Seq (logs): http://localhost:5380
- Jaeger (traces): http://localhost:16686

## Docs

- [C4 context diagram](docs/c4/context.png)
- [C4 container diagram](docs/c4/container.png)
- [ER diagram](docs/er/er-diagram.png)
