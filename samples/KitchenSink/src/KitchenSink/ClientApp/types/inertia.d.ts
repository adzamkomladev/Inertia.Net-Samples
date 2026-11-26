import type { App } from '@/wayfinder/types';

export interface SharedPageProps {
    name: string;
    auth: {
        user: App.Models.User | null;
    };
    sidebarOpen: boolean;
    queryMethod: boolean;
    demoResetAt: string | null;
}

export interface FlashData {
    [key: string]: unknown;
    message?: string;
    type?: string;
    parsedData?: Record<string, unknown>;
    receivedOrderId?: string;
}

export interface SharedProps extends SharedPageProps {}

declare module '@inertiajs/core' {
    interface InertiaConfig {
        sharedPageProps: SharedProps;
        flashDataType: FlashData;
        errorValueType: string;
    }
}

declare module 'vue' {
    interface ComponentCustomProperties {
        $page: {
            url: string;
            props: SharedProps;
            flash: FlashData;
        };
    }
}
