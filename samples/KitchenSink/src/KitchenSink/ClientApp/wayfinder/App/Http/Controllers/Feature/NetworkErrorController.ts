import { route } from '../../../../core';

export const httpExceptions = route('get', '/features/errors/http-exceptions');
export const httpException403 = route('get', '/features/errors/http-exceptions/403');
export const httpException404 = route('get', '/features/errors/http-exceptions/404');
export const httpException500 = route('get', '/features/errors/http-exceptions/500');
export const httpExceptionUnhandled = route(
    'get',
    '/features/errors/http-exceptions/unhandled',
);
export const networkErrors = route('get', '/features/errors/network-errors');
