<script setup lang="ts">
import { Head, Link, router } from '@inertiajs/vue3';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Navigation' },
    { title: 'View Transitions' },
];

const pages = [
    { title: 'Dashboard', href: '/dashboard' },
    { title: 'Contacts', href: '/contacts' },
    { title: 'Organizations', href: '/organizations' },
    { title: 'This Page', href: '/features/navigation/view-transitions' },
];
</script>

<template>
    <Head title="View Transitions" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="View Transitions"
                docs="the-basics/view-transitions"
                controller="app/Http/Controllers/Feature/NavigationController.php#L40"
            >
                Use the browser's native View Transitions API for smooth page
                navigation animations. Falls back to standard transitions in
                unsupported browsers.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- With View Transitions -->
                <FeatureCard title="With View Transitions">
                    <template #description>
                        These links use
                        <code class="rounded bg-muted px-1 py-0.5 text-xs"
                            >view-transition</code
                        >
                        for a smooth cross-fade animation.
                    </template>
                    <div class="space-y-2">
                        <Link
                            v-for="page in pages"
                            :key="page.href"
                            :href="page.href"
                            view-transition
                            class="flex items-center rounded-md border border-black/10 bg-background px-4 py-2 text-sm transition-colors hover:bg-accent dark:border-white/10"
                        >
                            {{ page.title }}
                        </Link>
                    </div>
                </FeatureCard>

                <!-- Without View Transitions -->
                <FeatureCard
                    title="Without View Transitions"
                    description="Standard navigation. No cross-fade animation."
                >
                    <div class="space-y-2">
                        <Link
                            v-for="page in pages"
                            :key="page.href"
                            :href="page.href"
                            class="flex items-center rounded-md border border-black/10 bg-background px-4 py-2 text-sm transition-colors hover:bg-accent dark:border-white/10"
                        >
                            {{ page.title }}
                        </Link>
                    </div>
                </FeatureCard>

                <!-- Programmatic -->
                <FeatureCard
                    info-card
                    class="lg:col-span-2"
                    title="Programmatic Visits"
                >
                    <template #description>
                        Use
                        <code class="rounded bg-muted px-1 py-0.5 text-xs"
                            >router.visit(url, {'{'} viewTransition: true
                            {'}'})</code
                        >
                        for programmatic navigation with transitions.
                    </template>
                    <div class="flex flex-wrap gap-2">
                        <Button
                            v-for="page in pages"
                            :key="page.href"
                            variant="outline"
                            @click="
                                router.visit(page.href, {
                                    viewTransition: true,
                                })
                            "
                        >
                            {{ page.title }}
                        </Button>
                    </div>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
