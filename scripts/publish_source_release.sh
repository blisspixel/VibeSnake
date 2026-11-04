#!/usr/bin/env bash
# Hosted publisher only: the workflow supplies artifacts from qualified main CI.
set -euo pipefail

: "${GITHUB_REPOSITORY:?}" "${GITHUB_RUN_ID:?}" "${GITHUB_RUN_ATTEMPT:?}" "${QUALIFIED_SHA:?}"
[[ "$QUALIFIED_SHA" =~ ^[0-9a-f]{40}$ ]]
[[ "$GITHUB_RUN_ID" =~ ^[0-9]+$ && "$GITHUB_RUN_ATTEMPT" =~ ^[0-9]+$ ]]
tag="player-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"
repo="$GITHUB_REPOSITORY"
assets="${1:?artifact directory}"
notes="${2:?release notes}"
# Release immutability is an administrator-managed repository prerequisite.
# GITHUB_TOKEN cannot read that setting (Administration:read is required).
# Verify the resulting release's immutable state with contents permission below.

# Creation can have succeeded before a connection failed. Reobserve the exact
# object instead of moving an existing tag or assuming a failed request did nothing.
gh api --method POST "repos/$repo/git/refs" \
  -f ref="refs/tags/$tag" -f sha="$QUALIFIED_SHA" >/dev/null 2>&1 || :
[[ "$(gh api "repos/$repo/git/ref/tags/$tag" --jq '[.object.type, .object.sha] | @tsv')" == $'commit\t'"$QUALIFIED_SHA" ]]
gh release create "$tag" --draft --prerelease --latest=false --verify-tag \
  --repo "$repo" --title "Vibe Snake source snapshot $tag" --notes-file "$notes" \
  >/dev/null 2>&1 || :
state="$(gh api --paginate "repos/$repo/releases?per_page=100" \
  --jq ".[] | select(.tag_name == \"$tag\")" | \
  jq -se 'if length == 1 then .[0] else error("Expected one release") end')"
jq -e --arg tag "$tag" '.tag_name == $tag and .prerelease == true' <<<"$state" >/dev/null

if [[ "$(jq -r '.draft' <<<"$state")" == true ]]; then
  # Draft assets are not distributed. Resuming a partial upload is safe only
  # here; the immutable published branch below never uploads or edits assets.
  gh release upload "$tag" "$assets"/* --clobber --repo "$repo"
fi
verify="$(mktemp -d)"
trap 'rm -rf "$verify"' EXIT
gh release download "$tag" --repo "$repo" --dir "$verify"
diff <(find "$assets" -maxdepth 1 -type f -printf '%f\n' | sort) \
     <(find "$verify" -maxdepth 1 -type f -printf '%f\n' | sort)
for file in "$assets"/*; do
  [[ -f "$file" && ! -L "$file" ]]
  cmp -- "$file" "$verify/$(basename "$file")"
done
(cd "$verify" && sha256sum --check SHA256SUMS.txt)
if [[ "$(jq -r '.draft' <<<"$state")" == true ]]; then
  gh release edit "$tag" --draft=false --prerelease --latest=false --repo "$repo"
fi
gh api "repos/$repo/releases/tags/$tag" \
  --jq '.draft == false and .prerelease == true and .immutable == true' | grep -qx true
printf 'Published verified immutable snapshot: %s\n' "$tag"
