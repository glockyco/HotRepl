/**
 * Canonical site identity — single source of truth for all SEO constants.
 */

export const SITE_URL = "https://hotrepl.glockyco.com";
export const SITE_NAME = "HotRepl";

export const OG_IMAGE_PATH = "/og-image.png";
export const OG_IMAGE_WIDTH = 1200;
export const OG_IMAGE_HEIGHT = 630;
export const OG_IMAGE_ALT = "HotRepl — Runtime C# REPL and typed command bridge for Unity games";
export const OG_LOCALE = "en_US";

/** Absolute canonical URL for a site-relative path (e.g. "/protocol/"). */
export function canonicalUrl(path: string): string {
  const normalised = path.endsWith("/") ? path : `${path}/`;
  return `${SITE_URL}${normalised}`;
}

export function ogImageUrl(): string {
  return `${SITE_URL}${OG_IMAGE_PATH}`;
}
