import { describe, expect, it, vi } from 'vitest';
import { PageMarkHistory } from './page-mark-history';
import { PageMarkDto } from './page-mark.models';

const mark = (id: string, color = '#000000'): PageMarkDto => ({ id, pageId: 'page', kind: 'Check',
  box: { x: .1, y: .2, width: .03, height: .04 }, color, strokeWidth: .08, revision: 0 });

describe('PageMarkHistory', () => {
  it('undoes and redoes creation, remapping IDs after re-creation', async () => {
    const history = new PageMarkHistory();
    history.record(null, mark('a'));
    const apply = vi.fn().mockResolvedValueOnce(null).mockResolvedValueOnce(mark('b'));
    await history.undo(apply);
    expect(history.canRedo).toBe(true);
    await history.redo(apply);
    expect(apply).toHaveBeenNthCalledWith(1, null, 'a');
    expect(apply).toHaveBeenNthCalledWith(2, mark('a'), 'a');
    expect(history.canUndo).toBe(true);
  });

  it('leaves cursor unchanged on failure and clears redo after a new change', async () => {
    const history = new PageMarkHistory();
    history.record(mark('a'), mark('a', '#FF0000'));
    await expect(history.undo(async () => { throw new Error('network'); })).rejects.toThrow('network');
    expect(history.canUndo).toBe(true);
    expect(history.canRedo).toBe(false);
    await history.undo(async () => mark('a'));
    history.record(mark('a'), mark('a', '#00FF00'));
    expect(history.canRedo).toBe(false);
  });

  it('restores a deleted mark with a new ID, then deletes that new ID on redo', async () => {
    const history = new PageMarkHistory();
    history.record(mark('old'), null);
    const apply = vi.fn().mockResolvedValueOnce(mark('new')).mockResolvedValueOnce(null);
    await history.undo(apply);
    await history.redo(apply);
    expect(apply).toHaveBeenNthCalledWith(1, mark('old'), 'old');
    expect(apply).toHaveBeenNthCalledWith(2, null, 'new');
  });
});
