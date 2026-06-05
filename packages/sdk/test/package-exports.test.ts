import { describe, expect, it } from "bun:test";
import { access, readdir, readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

interface PackageJson {
  name: string;
  private?: boolean;
  files?: string[];
  exports?: unknown;
}

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), "../../..");

async function publicWorkspacePackages(): Promise<PackageJson[]> {
  const packagesDir = join(repoRoot, "packages");
  const packageDirs = await readdir(packagesDir);
  const packages: PackageJson[] = [];

  for (const packageDir of packageDirs) {
    const packageJsonPath = join(packagesDir, packageDir, "package.json");
    try {
      await access(packageJsonPath);
    } catch {
      continue;
    }
    const packageJson = JSON.parse(await readFile(packageJsonPath, "utf8")) as PackageJson;
    if (packageJson.private === true) continue;
    packages.push(packageJson);
  }

  return packages.sort((left, right) => left.name.localeCompare(right.name));
}

function collectExportTargets(exportsField: unknown): string[] {
  if (typeof exportsField === "string") return [exportsField];
  if (typeof exportsField !== "object" || exportsField === null) return [];

  return Object.values(exportsField).flatMap((value) => collectExportTargets(value));
}

function collectBunExportTargets(exportsField: unknown): string[] {
  if (typeof exportsField !== "object" || exportsField === null) return [];
  if ("bun" in exportsField && typeof exportsField.bun === "string") return [exportsField.bun];
  return Object.values(exportsField).flatMap((value) => collectBunExportTargets(value));
}

function isPublishedTarget(target: string, files: string[]): boolean {
  if (!target.startsWith("./")) return true;
  const relativeTarget = target.slice(2);
  return files.some((publishedPath) => {
    const normalized = publishedPath.replace(/^\.\//, "").replace(/\/$/, "");
    return relativeTarget === normalized || relativeTarget.startsWith(`${normalized}/`);
  });
}

describe("published package exports", () => {
  it("only point at files included by npm package files", async () => {
    const violations: string[] = [];

    for (const packageJson of await publicWorkspacePackages()) {
      const files = packageJson.files ?? [];
      for (const target of collectExportTargets(packageJson.exports)) {
        if (!isPublishedTarget(target, files)) {
          violations.push(
            `${packageJson.name} exports ${target}, but files only includes ${files.join(", ")}`,
          );
        }
      }
    }

    expect(violations).toEqual([]);
  });

  it("uses built JavaScript for published Bun entrypoints", async () => {
    const violations: string[] = [];

    for (const packageJson of await publicWorkspacePackages()) {
      for (const target of collectBunExportTargets(packageJson.exports)) {
        if (!target.startsWith("./dist/")) {
          violations.push(
            `${packageJson.name} has Bun export ${target}; published Bun entrypoints must use dist`,
          );
        }
      }
    }

    expect(violations).toEqual([]);
  });
});
