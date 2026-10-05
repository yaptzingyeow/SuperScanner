import { defineConfig } from 'vitest/config';

// The workspace specs render the whole editor; under a full parallel run the first one can pass 5 s.
export default defineConfig({
  test: { testTimeout: 15_000 },
});
