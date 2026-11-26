# 🗞 TechDigest

A daily tech news email, delivered every morning at 07:00 (Istanbul time).

TechDigest pulls RSS feeds from BBC, The New York Times, The Verge, TechCrunch, Ars Technica,
SCMP, NVIDIA, OpenAI and Google News (which surfaces Reuters, Bloomberg, CNBC and more),
picks the most relevant stories about **AI, chips & US–China, big tech markets, software,
security and regulation**, and emails them as a clean digest.

Stories are in **English** (great daily reading practice) with a short **Turkish summary**
under each one, generated with the free tier of the Gemini API.

Runs entirely on free services: GitHub Actions + Gmail SMTP + Gemini free tier.

## How it works

```
cron-job.org (07:00 Istanbul) → GitHub Actions (workflow_dispatch)
  → FeedFetcher      fetch all feeds in parallel, keep the last 24 hours
  → Deduplicator     drop the same story reported by multiple outlets
  → Categorizer      keyword scoring → topics, filter out stock-tip clickbait
  → GeminiSummarizer one request → 1–2 sentence Turkish summary per story (optional)
  → EmailRenderer    HTML + plain-text email
  → EmailSender      Gmail SMTP (MailKit)
```

| Path | Purpose |
|---|---|
| `src/TechDigest/config/sources.json` | RSS feeds — add a line to add a source |
| `src/TechDigest/config/topics.json` | Topics, keywords, limits, excluded phrases |
| `src/TechDigest/Services/` | One class per step above |
| `tests/TechDigest.Tests/` | xUnit tests (no network needed) |
| `.github/workflows/daily-digest.yml` | Daily schedule |

## Run locally

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet test
dotnet run --project src/TechDigest -- --dry-run   # writes digest.html, sends nothing
```

To send a real email, set the environment variables below and run without `--dry-run`.

## Setup

1. **Gmail app password** — enable 2-Step Verification on the sending Gmail account, then create an
   app password at <https://myaccount.google.com/apppasswords>.
2. **Gemini API key (free, optional)** — create one at <https://aistudio.google.com/apikey>.
   Without it, the digest is sent in English only.
3. **Repository secrets** — *Settings → Secrets and variables → Actions → New repository secret*:

   | Secret | Value |
   |---|---|
   | `GMAIL_ADDRESS` | the Gmail address that sends the digest |
   | `GMAIL_APP_PASSWORD` | the 16-character app password |
   | `TO_ADDRESS` | where the digest should arrive — separate multiple addresses with commas (sent as BCC) |
   | `GEMINI_API_KEY` | Gemini key (optional) |

4. **Test it** — *Actions → Daily TechDigest → Run workflow* (tick *Dry run* to skip sending).

## Schedule

GitHub's built-in scheduler delayed runs by 8–10 hours, so the digest is triggered externally:

1. Create a **fine-grained token** at <https://github.com/settings/personal-access-tokens/new>
   — *Only select repositories → TechDigest*, permission *Actions: Read and write*, expiry 1 year
   (renew it when it expires).
2. On [cron-job.org](https://cron-job.org) create a job:
   - URL: `https://api.github.com/repos/yasinkocakdev/TechDigest/actions/workflows/daily-digest.yml/dispatches`
   - Every day at 07:00, time zone `Europe/Istanbul`
   - Method `POST`, body `{"ref":"main"}`
   - Headers: `Authorization: Bearer <token>`, `Accept: application/vnd.github+json`,
     `X-GitHub-Api-Version: 2022-11-28`, `Content-Type: application/json`
   - A successful call returns `204`.

GitHub's scheduler (08:00 Istanbul) stays as a **backup**: it checks whether a "Send digest"
run already succeeded today and skips itself if so.

Optional: set a `GEMINI_MODEL` environment variable to pin a model. Otherwise the newest stable
`flash` model available to your key is picked automatically (so retired models don't break the digest).

> **Note:** GitHub pauses the backup schedule in public repos after 60 days without commits.
> The cron-job.org trigger keeps working regardless.
