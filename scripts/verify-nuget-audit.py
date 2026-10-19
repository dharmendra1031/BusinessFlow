"""Fail CI when NuGet reports vulnerable direct or transitive dependencies."""

import json
import sys


def main(path: str) -> None:
    with open(path, encoding="utf-8") as source:
        report = json.load(source)

    findings = [
        (project.get("path"), package.get("id"), package.get("resolvedVersion"), issue)
        for project in report.get("projects", [])
        for framework in project.get("frameworks", [])
        for package_type in ("topLevelPackages", "transitivePackages")
        for package in framework.get(package_type, [])
        for issue in package.get("vulnerabilities", [])
    ]
    for project, name, version, issue in findings:
        print(
            f"{project}: {name} {version} - "
            f"{issue.get('severity', 'unknown')} - {issue.get('advisoryurl', '')}"
        )

    if findings:
        raise SystemExit(f"NuGet audit failed: {len(findings)} vulnerabilities")

    print("NuGet audit passed: no known vulnerable packages.")


if __name__ == "__main__":
    main(sys.argv[1])
