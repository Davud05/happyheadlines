# Happy Headlines

Start everything with:

```powershell
docker compose up -d --build
```

On first start (empty databases) the services insert example articles for every continent, comments and drafts.
To start over from scratch: `docker compose down -v` and then `docker compose up -d --build`.

## CI/CD

Every push to `main` runs [`.github/workflows/ci-cd.yml`](.github/workflows/ci-cd.yml):

1. **build-and-test**: builds the solution and runs `tests/HappyHeadlines.Tests`. If anything fails, nothing is delivered.
2. **images**: builds one image per service and pushes it to GitHub Container Registry as `ghcr.io/davud05/happyheadlines-<service>`, tagged `1.0.<run number>`, `sha-<commit>` and `latest`.

Pull requests against `main` run the same steps but never push images.

Run a delivered version instead of building locally (rolling back is the same command with an older version):

```powershell
$env:TAG = "1.0.12"
docker compose pull
docker compose up -d --no-build
```

Run the tests locally with `dotnet test HappyHeadlines.slnx`.

## Services

- Website (read articles and comment): http://localhost:5000

- ArticleService (load balancer): http://localhost:8000/api/articles
- ArticleService, one continent: http://localhost:8000/api/articles?continent=Europe
- CommentService: http://localhost:8001/api/comments?articleId=a0000000-0000-0000-0000-000008000001
- ProfanityService: http://localhost:8002/api/profanity/words
- DraftService: http://localhost:8003/api/drafts
- Webapp (write and publish articles): http://localhost:5001
- PublisherService: `POST` http://localhost:8004/api/publish
- NewsletterService (send daily newsletter now): `POST` http://localhost:8005/api/newsletter/daily
- SubscriberService (behind the release toggle): http://localhost:8006/api/subscribers, subscribe with `POST {"email": "..."}`
- Website subscribe page (only while the toggle is on): http://localhost:5000/Subscribe
- FeatureHub (release toggles): http://localhost:8085

## Messaging

- RabbitMQ (ArticleQueue, SubscriberQueue), login guest/guest: http://localhost:15672
- Mailpit (sent newsletters): http://localhost:8025

## Release toggle: SubscriberService

SubscriberService is deployed but hidden behind the `SUBSCRIBER_SERVICE` toggle in FeatureHub. The services stream the toggle from FeatureHub, so switching it takes effect within a second, without restarting or redeploying anything.

| | Toggle off | Toggle on |
| --- | --- | --- |
| SubscriberService `/api/subscribers` | `503` | available |
| Website | no Newsletter link, `/Subscribe` is `404` | Newsletter link and subscribe page |
| NewsletterService recipients | configured list (`Newsletter__Subscribers__*` in compose) | its own copy of SubscriberService's subscribers |

The toggle counts as off until FeatureHub has answered at least once, for example when no API key is configured. After that each service keeps the last value it received, so FeatureHub going down changes nothing. No service `depends_on` FeatureHub.

### One-time FeatureHub setup

FeatureHub keeps its data in the `featurehub` volume, so this is only needed on a fresh start (e.g. after `docker compose down -v`).

1. Open http://localhost:8085 and create the first admin user, the organisation and a portfolio (e.g. "Happy Headlines").
2. Create an application named `HappyHeadlines`. FeatureHub creates a `production` environment for it.
3. In the application, create a feature with key `SUBSCRIBER_SERVICE` of type standard flag (boolean).
4. Under the portfolio's **Service Accounts**, create a service account (e.g. `happyheadlines-services`) with **Read** permission on the `production` environment.
5. On the **API Keys** page, choose the `production` environment and copy the **client evaluated** API key. It looks like `<environment id>/<key>*<key>`.
6. Put it in a `.env` file next to `docker-compose.yml` (it is git-ignored) and recreate the services that read toggles:

   ```powershell
   Set-Content .env "FEATUREHUB_API_KEY=<the key>"
   docker compose up -d subscriber-service newsletter-service website
   ```

To switch the toggle, open the feature in the `production` environment, unlock it (new features start locked), and turn it on or off. Each service logs `Feature toggle SUBSCRIBER_SERVICE is now ON/OFF` in Seq.

### Fault isolation between NewsletterService and SubscriberService

The two services are in separate swimlanes (own networks `subscriber-lane` / `newsletter-lane` and own databases) and never call each other. SubscriberService publishes a `SubscriptionChanged` event to SubscriberQueue for every subscribe and unsubscribe; NewsletterService keeps its own copy of the subscribers in `newsletter-db`.

- SubscriberService or its database down: newsletters still go out to the last known subscribers.
- NewsletterService or its database down: subscribing still works; the events wait in SubscriberQueue and are applied when it is back.
- RabbitMQ down: SubscriberService only commits a subscription after RabbitMQ has confirmed the event, so the request fails with `503` after 5 seconds and nothing is saved.

Known limitation (no outbox): if the event reaches RabbitMQ but the request still fails (the database commit fails right after, or the confirmation arrives after the 5 second timeout), NewsletterService receives an event for a subscription that was not saved, and the caller gets an error. Events are applied idempotently per email address, so retrying is harmless.

## Caching

- ArticleCache (Redis, `article-cache`): offline cache with all articles from the last 14 days, loaded by ArticleService on startup and every 5 minutes.
- CommentCache (Redis, `comment-cache`): filled on cache miss, capped at 64 MB and cleaned with LRU.

Responses from both services carry an `X-Cache: HIT` or `X-Cache: MISS` header. If a cache is down, the service falls back to its database.

## Monitoring

- Seq (logs): http://localhost:5380
- Jaeger (traces): http://localhost:16686
- Grafana (cache hit ratio dashboard): http://localhost:3000
- Prometheus (metrics): http://localhost:9090

Only failed (5xx) and slow (> `Logging__SlowRequestMs`, default 1000 ms) requests are logged, and 10 % of traces are
kept (`Otlp__SampleRatio`). Set `Otlp__SampleRatio: "1.0"` in `docker-compose.yml` to trace everything while debugging.

## Docs

- [C4 context diagram](docs/c4/context.png)
- [C4 container diagram](docs/c4/container.png)
- [ER diagram](docs/er/er-diagram.png)
- [Week 45: green architecture tactics and measurements](docs/sustainability/week45.md)
