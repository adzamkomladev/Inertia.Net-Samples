import { route } from '../core';

const contacts = {
    index: route('get', '/contacts'),
    create: route('get', '/contacts/create'),
    store: route('post', '/contacts'),
    show: route('get', '/contacts/{contact}', [{ name: 'contact' }]),
    edit: route('get', '/contacts/{contact}/edit', [{ name: 'contact' }]),
    update: route('put', '/contacts/{contact}', [{ name: 'contact' }]),
    destroy: route('delete', '/contacts/{contact}', [{ name: 'contact' }]),
    favorite: route('post', '/contacts/{contact}/favorite', [
        { name: 'contact' },
    ]),
    notes: {
        store: route('post', '/contacts/{contact}/notes', [
            { name: 'contact' },
        ]),
    },
};

export default contacts;
