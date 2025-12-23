-- Sample SharedLinkType data for Cable EV Charging System
-- Run this SQL script to populate your SharedLinkType table

INSERT INTO SharedLinkType (TypeName, Description, BaseUrl, IsActive) VALUES
('charging-point', 'Share a specific charging point with others', 'https://yourdomain.com/charging-point', 1),
('user-profile', 'Share user profile information', 'https://yourdomain.com/profile', 1),
('charging-history', 'Share charging session history', 'https://yourdomain.com/history', 1),
('station-review', 'Share station review and rating', 'https://yourdomain.com/review', 1),
('promotion', 'Share promotional offers and deals', 'https://yourdomain.com/promo', 1),
('route-plan', 'Share planned charging route', 'https://yourdomain.com/route', 1),
('emergency-contact', 'Emergency contact sharing for charging assistance', 'https://yourdomain.com/emergency', 1);

-- Verify the inserted data
SELECT * FROM SharedLinkType WHERE IsActive = 1;