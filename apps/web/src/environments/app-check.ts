// Safe default for committed and production builds. Development replaces this
// file with the git-ignored app-check.local.ts containing the local debug token.
export const appCheckDebugToken: boolean | string = false;
