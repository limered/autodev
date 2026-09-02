import { secondsSince, lastSeenLabel } from "../../_shared/models/time.js";

export function hostView(host, nowMs) {
  const secs = secondsSince(host?.lastSeen, nowMs);
  const online = host?.online === true;

  return {
    online,
    label: online ? "host online" : "host offline",
    lastSeen: lastSeenLabel(secs),
    freshnessClass: online ? "fresh" : secs === null ? "unknown" : "stale-danger",
  };
}
