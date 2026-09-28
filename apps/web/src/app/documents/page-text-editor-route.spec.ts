import { foundationRoutes } from '../app.routes.base';

describe('Page text editor route', () => {
  it('opens the selected document page in its own editor', () => {
    const shell = foundationRoutes.find((route) => route.path === '');
    const editor = shell?.children?.find((route) => route.path === 'documents/:documentId/pages/:pageId/text');
    expect(editor?.component).toBeTruthy();
    expect(editor?.canDeactivate?.length).toBe(1);
  });
});
