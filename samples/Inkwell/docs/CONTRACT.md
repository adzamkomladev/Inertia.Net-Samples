# Inkwell feature contracts

Feature routes own HTTP and Inertia responses. Services return POCOs/records; types live with the owning feature. Existing URLs, prop names and authorization policies remain stable. See docs/SOCIAL_FEATURES.md for social behavior.


## Background delivery and feature boundaries

Services return records/data; routes and response filters own HTTP/Inertia conversion. Realtime triggers persist jobs and return after enqueueing. Workers deliver with retries; HTTP success does not promise immediate delivery. Upload confirmation checks storage metadata before making an asset ready; rejected-object cleanup is queued in the presigned-upload samples. Existing route URLs and prop names remain unchanged.
