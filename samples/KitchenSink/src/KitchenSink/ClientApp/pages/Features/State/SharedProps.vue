<script setup lang="ts">
import { Head } from '@inertiajs/vue3';
import CodeBlock from '@/components/CodeBlock.vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'State Management' },
    { title: 'Shared Props' },
];
</script>

<template>
    <Head title="Shared Props" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Shared Props"
                docs="data-props/shared-data"
                controller="app/Http/Controllers/Feature/StateController.php#L42"
            >
                Global data shared across all pages via
                <code class="text-xs">HandleInertiaRequests</code> middleware.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Shared props display -->
                <FeatureCard title="Current Shared Props">
                    <template #description>
                        Accessed via <code class="text-xs">usePage()</code>.
                        Available on every page without explicitly passing them.
                    </template>
                    <div class="space-y-4">
                        <div class="space-y-3">
                            <div class="flex items-center justify-between">
                                <span class="text-sm font-medium"
                                    >App Name</span
                                >
                                <Badge variant="secondary">{{
                                    $page.props.name
                                }}</Badge>
                            </div>
                            <div class="flex items-center justify-between">
                                <span class="text-sm font-medium"
                                    >Auth User</span
                                >
                                <Badge variant="outline" class="text-xs">{{
                                    $page.props.auth?.user?.name ?? 'null'
                                }}</Badge>
                            </div>
                            <div class="flex items-center justify-between">
                                <span class="text-sm font-medium"
                                    >User Email</span
                                >
                                <Badge
                                    variant="outline"
                                    class="font-mono text-xs"
                                    >{{
                                        $page.props.auth?.user?.email ?? 'null'
                                    }}</Badge
                                >
                            </div>
                            <div class="flex items-center justify-between">
                                <span class="text-sm font-medium"
                                    >Sidebar Open</span
                                >
                                <Badge
                                    :variant="
                                        $page.props.sidebarOpen
                                            ? 'default'
                                            : 'secondary'
                                    "
                                    >{{ $page.props.sidebarOpen }}</Badge
                                >
                            </div>
                        </div>
                    </div>
                </FeatureCard>

                <!-- usePage() raw data -->
                <FeatureCard info-card title="usePage() Data">
                    <template #description>
                        The full <code class="text-xs">usePage()</code> object
                        includes props, url, component, and more.
                    </template>
                    <div class="space-y-3">
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">URL</span>
                            <Badge
                                variant="outline"
                                class="font-mono text-xs"
                                >{{ $page.url }}</Badge
                            >
                        </div>
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">Component</span>
                            <Badge
                                variant="outline"
                                class="font-mono text-xs"
                                >{{ $page.component }}</Badge
                            >
                        </div>
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">Version</span>
                            <Badge variant="secondary">{{
                                $page.version ?? 'null'
                            }}</Badge>
                        </div>
                    </div>
                </FeatureCard>

                <!-- Server config -->
                <FeatureCard
                    info-card
                    class="lg:col-span-2"
                    title="Server Configuration"
                    description="How shared props are defined in the HandleInertiaRequests middleware."
                >
                    <CodeBlock
                        code="// app/Http/Middleware/HandleInertiaRequests.php

public function share(Request $request): array
{
    return [
        ...parent::share($request),
        'name' => config('app.name'),
        'auth' => [
            'user' => $request->user(),
        ],
        'sidebarOpen' => ! $request->hasCookie('sidebar_state')
            || $request->cookie('sidebar_state') === 'true',
    ];
}"
                    />
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
