# Release Signing and Provenance

Status: signing boundary and readiness gate implemented; publisher credentials and final platform signing are not configured.

## Trust boundary

Ordinary CI builds, tests, exports, smokes, and hashes unsigned players. It has no signing-secret references and no OIDC attestation permission. `config/release_signing_policy.json` is a strict non-secret policy, not a credential file. Source policy rejects common certificate, private-key, keystore, and environment-secret files, while artifact inspection rejects those formats from player bundles.

Each exported player produces `release-signing-readiness-v1` outside the install tree. It binds the unsigned state, platform route, promotion decision, and required verification set to the SHA-256 of `artifact-manifest.json`. Debug builds and builds without a full source revision pass qualification but are explicitly not promotable.

Tag and manual CI runs use a separate least-privilege job to create GitHub OIDC/Sigstore provenance for each qualified unsigned manifest. This proves which workflow qualified the input. It is not a Windows signature, Apple notarization, or final release attestation.

## Required promotion sequence

1. Build a clean `Release` artifact from one full source revision on the native platform runner.
2. Complete the same read-only install, smoke, payload, content, and manifest gates used by ordinary CI.
3. Enter the protected platform release environment. Import or access signing identity only there.
4. Sign the exact policy targets and complete every platform verification below.
5. Regenerate post-signing file hashes and checksums because signing changes artifact bytes.
6. Re-run smoke and artifact inspection against the final bytes.
7. Create final provenance for the post-signing manifest or distributable, then verify that provenance before promotion.
8. Retain the unsigned input digest, final digest, signature evidence, provenance bundle, and promotion decision together.

No step may copy a certificate, private key, password, API key, or notarization credential into source, logs, manifests, qualification evidence, workflow artifacts, or the player bundle.

## Platform routes

### Windows x64

- Sign `VibeSnake.exe` with Authenticode SHA-256 in the protected Windows release environment.
- Add the eventual installer to the signable target list only when its store/archive contract exists.
- Use a trusted timestamp and verify with SignTool policy verification.
- Generate checksums only after signing.

Microsoft documents SignTool signing, timestamping, verification, and return codes in [SignTool.exe](https://learn.microsoft.com/en-us/dotnet/framework/tools/signtool-exe).

### macOS Universal

- Expand `VibeSnake.zip` and sign nested executable code followed by `Vibe Snake.app` with Developer ID.
- Enable hardened runtime and use only reviewed entitlements.
- Verify the strict code-signing graph and hardened-runtime state.
- Submit with `notarytool`, require an accepted result, staple the ticket, validate the staple, and assess with Gatekeeper.
- Recreate the archive and checksums only after signing and stapling.

Apple requires Developer ID signing, hardened runtime, secure timestamping, notarization, and ticket handling for direct distribution. See [Notarizing macOS software before distribution](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution).

### Linux x64

- Do not claim a platform code signature that the release channel does not provide.
- Verify the launcher and executable permission contract, artifact checksums, dependency/runtime baseline, and final provenance.
- Sign store metadata or repository packages only when a selected channel defines that contract.

## Provenance

GitHub artifact attestations use a short-lived OIDC identity and bind an artifact digest to workflow provenance. The dedicated job follows the current [GitHub artifact attestation guidance](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations). Consumers must verify the attestation and expected repository/workflow identity; creating an attestation alone is not a promotion decision.

The qualification workflow currently attests unsigned input manifests on tag and manual runs. Final release provenance remains gated behind protected platform signing and post-signing inspection.

## Machine-readable authorities

| Authority | Purpose |
| --- | --- |
| `config/release_signing_policy.json` | Exact platform routes and non-secret boundary flags |
| `ReleaseSigningPolicy.cs` | Strict parser, anti-weakening rules, and artifact readiness evaluation |
| `release_signing_readiness.json` | Per-export unsigned input state and promotion eligibility |
| `artifact-manifest.json` | Source/toolchain identity and exact artifact file hashes |
| `release_output_plan.json` | Direct-download/store-depot shape, deterministic package hash, and publication blockers |
| Detached Sigstore bundle | Workflow provenance for the attested manifest |

## Remaining release work

- Configure protected platform environments with required reviewers and least-privilege publisher identities.
- Implement credential-provider-specific signing without exposing secret values to command output.
- Add the final signed-output verification and post-signing manifest stages.
- Select installer/store channels and extend signable targets only for their exact outputs.
- Run and retain the complete Windows, macOS, and Linux release-candidate evidence chain.
