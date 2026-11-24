// A folder type for the browser test, written as a browser loads it: the notes of a folder, and adding one.
import { createElement as element } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button, Icon, PageLayout, definePlugin, iconMap, toast, useLoaded } from '@byteshelf/plugin';

function Album({ folder, contents, canWrite }) {
  const { data } = useQuery({
    queryKey: [...contents.queryKey, 'notes'],
    queryFn: ({ signal }) => contents.list(undefined, signal),
  });
  const files = data?.files ?? [];

  async function add() {
    try {
      const number = files.length + 1;
      await contents.store(`note-${number}.txt`, new Blob([`Note ${number}`], { type: 'text/plain' }));
    } catch (failed) {
      toast.error(String(failed));
    }
  }

  return element(PageLayout, null,
    element('h1', null, `Album of ${folder.name}`),
    files.map((file) => element(Note, { key: file.id, file, contents })),
    canWrite && element(Button, { onClick: () => void add() }, 'Add a note'));
}

function Note({ file, contents }) {
  // Not a query, which would keep its decrypted text after the viewer is gone.
  const loaded = useLoaded(contents, async (signal) => {
    const reader = contents.read(file);
    try {
      return await (await reader.blob(signal)).text();
    } finally {
      reader.close();
    }
  });

  return loaded.status === 'done' && element('button', { onClick: () => contents.open(file) }, loaded.value);
}

function AlbumIcon(props) {
  return element('span', { 'data-testid': 'album-icon' }, element(Icon, { icon: iconMap.file, size: props.width }));
}

export default definePlugin({ folderTypes: [{ suffix: 'album', label: 'Album', icon: AlbumIcon, component: Album }] });
