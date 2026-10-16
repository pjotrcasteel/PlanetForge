import { readFile } from 'node:fs/promises';
import { REPORT_MARKER, summarizeBaseline } from './render-baseline-report.mjs';

const token = process.env.GITHUB_TOKEN;
const repository = process.env.GITHUB_REPOSITORY;
const runId = process.env.GITHUB_RUN_ID;
if (!token || !/^[\w.-]+\/[\w.-]+$/.test(repository ?? '') || !/^\d+$/.test(runId ?? '')) {
  throw new Error('GitHub Actions repository, run id and token are required');
}
const raw = await readFile(process.env.PLANETFORGE_PERF_OUTPUT ?? 'artifacts/performance-baseline.json', 'utf8');
const bundle = JSON.parse(raw);
const runUrl = 'https://github.com/' + repository + '/actions/runs/' + runId;
const body = summarizeBaseline(bundle, runUrl);
const api = 'https://api.github.com/repos/' + repository;
const headers = {
  Authorization: 'Bearer ' + token,
  Accept: 'application/vnd.github+json',
  'X-GitHub-Api-Version': '2022-11-28',
  'Content-Type': 'application/json',
};
async function request(method, endpoint, payload = null) {
  const result = await fetch(api + endpoint, { method, headers, body: payload === null ? undefined : JSON.stringify(payload) });
  if (!result.ok) throw new Error('GitHub API ' + method + ' ' + endpoint + ': HTTP ' + result.status + ' ' + (await result.text()).slice(0, 500));
  return result.json();
}
const issue = 239;
const comments = await request('GET', '/issues/' + issue + '/comments?per_page=100');
const existing = comments.find(c => c.body?.includes(REPORT_MARKER) && c.user?.login === 'github-actions[bot]');
if (existing) {
  await request('PATCH', '/issues/comments/' + existing.id, { body });
  console.log('Updated performance baseline issue comment ' + existing.id);
} else {
  const comment = await request('POST', '/issues/' + issue + '/comments', { body });
  console.log('Created performance baseline issue comment ' + comment.id);
}
