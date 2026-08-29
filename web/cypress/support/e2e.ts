/**
 * What every browser test has available.
 *
 * Two imports and nothing else. `cypress-real-events` adds the commands that go
 * through the browser's own input handling; `cypress-axe` adds the accessibility
 * check. Both are used in nearly every spec, so importing them here rather than
 * per file keeps the specs about what they are testing.
 */

import "cypress-real-events";
import "cypress-axe";
import "./commands";
