// Fixed workload for the week 45 measurements. Run through scripts/measure.ps1.
// A constant arrival rate keeps the amount of work identical between runs, however fast the system answers.
import http from 'k6/http';
import exec from 'k6/execution';
import { check } from 'k6';

const WEBSITE = 'http://website:8080';
const ARTICLES = 'http://article-lb';
const COMMENTS = 'http://comment-service:8080';
const PUBLISHER = 'http://publisher-service:8080';
const REGIONS = ['Global', 'Europe', 'Asia', 'Africa', 'NorthAmerica', 'SouthAmerica', 'Oceania', 'Antarctica'];

export const options = {
  scenarios: {
    readers: {
      executor: 'constant-arrival-rate',
      rate: 20,
      timeUnit: '1s',
      duration: '60s',
      preAllocatedVUs: 20,
      maxVUs: 100,
    },
  },
  summaryTrendStats: ['avg', 'med', 'p(95)', 'max'],
};

export function setup() {
  const articles = http.get(`${ARTICLES}/api/articles?continent=Global&limit=20`).json();
  return { ids: articles.map((a) => a.id) };
}

export default function (data) {
  const i = exec.scenario.iterationInTest;
  const region = REGIONS[i % REGIONS.length];
  const articleId = data.ids[i % data.ids.length];

  // Every iteration: a reader opens the front page and one article with its comments.
  check(http.get(`${WEBSITE}/?region=${region}`), { 'front page 200': (r) => r.status === 200 });
  check(http.get(`${WEBSITE}/Article/Global/${articleId}`), { 'article 200': (r) => r.status === 200 });

  // Every 5th iteration: a comment (goes through ProfanityService and invalidates the CommentCache).
  if (i % 5 === 0) {
    const body = JSON.stringify({ articleId, author: 'Load test', content: `Lovely story number ${i}` });
    check(http.post(`${COMMENTS}/api/comments`, body, { headers: { 'Content-Type': 'application/json' } }),
      { 'comment accepted': (r) => r.status === 201 || r.status === 202 });
  }

  // Every 50th iteration: a published article (ArticleQueue, ArticleService, breaking-news newsletter).
  if (i % 50 === 0) {
    const body = JSON.stringify({ title: `Load test article ${i}`, content: 'Good things keep happening.', author: 'Load test', continent: region });
    check(http.post(`${PUBLISHER}/api/publish`, body, { headers: { 'Content-Type': 'application/json' } }),
      { 'publish accepted': (r) => r.status === 202 });
  }
}
