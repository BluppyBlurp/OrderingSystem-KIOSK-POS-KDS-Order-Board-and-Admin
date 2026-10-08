// Saves the running API's OpenAPI document as openapi.json (committed, so the frontend builds without the API).
// Usage: start the API (dotnet run --project backend/src/Kiosk.Api), then `pnpm gen:api` from frontend/.
import { writeFile } from "node:fs/promises";

const url = process.env.OPENAPI_URL ?? "http://localhost:5202/openapi/v1.json";
const res = await fetch(url);
if (!res.ok) throw new Error(`GET ${url} failed: ${res.status}. Is the API running in Development?`);
await writeFile(new URL("../openapi.json", import.meta.url), JSON.stringify(await res.json(), null, 2) + "\n");
console.log(`Saved ${url}`);
