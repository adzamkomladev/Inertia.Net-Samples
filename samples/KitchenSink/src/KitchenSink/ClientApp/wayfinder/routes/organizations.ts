import { route } from '../core';

const organizations = {
    index: route('get', '/organizations'),
    show: route('get', '/organizations/{organization}', [
        { name: 'organization' },
    ]),
    update: route('put', '/organizations/{organization}', [
        { name: 'organization' },
    ]),
};

export default organizations;
