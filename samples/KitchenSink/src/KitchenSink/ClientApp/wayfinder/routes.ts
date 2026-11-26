import { route } from './core';

export const home = route('get', '/');
export const dashboard = route('get', '/dashboard');
export const logout = route('post', '/logout');
